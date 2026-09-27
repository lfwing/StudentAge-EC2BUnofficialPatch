using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using Config;
using EC2BUnofficialPatch.Core;
using EC2BUnofficialPatch.Features.Mechanics;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EC2BUnofficialPatch.Services.EventCompatibility
{
    /// <summary>Runs after UP's enhanced CFG selection, before native first-wins merging.</summary>
    internal static class EventIdCompatibilityService
    {
        private static bool _enabled;
        private static bool _attempted;
        private static bool _standalone;
        private static CollisionPlan _plan;
        private static Dictionary<string, HashSet<int>> _baseline;
        private static readonly Dictionary<string, string> PathsByMod = new Dictionary<string, string>();

        // Deliberately retain the standalone prototype's durable identity registry.
        private static string RegistryPath => Path.Combine(Paths.ConfigPath, "StudentAge.EventCompat", "id-map.json");
        internal static bool Applied => _plan != null;

        internal static void Initialize()
        {
            _enabled = PluginConfig.EventIdCompatibility?.Value == true;
            _standalone = AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
                assembly.GetType("StudentAge.EventCompat.Plugin", false) != null);
            if (_standalone)
                PatchLog.Warning("机制模块-检测到独立事件兼容补丁，UP 不重复映射；普通 CFG 热重载暂停。请移除独立 DLL，开启 UP 的多mod事件ID兼容并重启。");
            else if (_enabled)
                PatchLog.Registration("机制模块-多mod事件ID兼容已启用：新增事件隔离，原版/DLC覆盖保持原规则。");
        }

        internal static void BeforeLoad(ModCtrl loader, ulong modId, ref string path)
        {
            if (!_enabled || _standalone) return;
            if (!_attempted)
            {
                _attempted = true;
                try { Prepare(loader, modId, path); }
                catch (Exception exception)
                {
                    _plan = null;
                    PathsByMod.Clear();
                    PatchLog.Error("机制模块-事件ID兼容未应用，本次保留原版加载规则；请修复后重启：" + exception);
                }
            }
            if (_plan == null) return;
            if (!PathsByMod.TryGetValue(modId.ToString(), out string transformed))
                throw new InvalidOperationException("事件兼容计划生成后 Mod 集合发生变化，请重启游戏：" + modId);
            path = transformed;
        }

        private static void Prepare(ModCtrl loader, ulong currentId, string currentPath)
        {
            var raw = AccessTools.Field(typeof(ModCtrl), "cfgMaps").GetValue(loader)
                as Dictionary<string, Dictionary<int, object>>;
            if (raw == null || raw.Any(pair => pair.Value.Count != 0))
                throw new InvalidOperationException("介入 Mod CFG 加载过晚，无法可靠捕获原版基线。");
            var baseline = new Dictionary<string, HashSet<int>>();
            foreach (string table in CollisionPlan.Tables)
            {
                var map = typeof(Cfg).GetProperty(table + "Map", BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null, null) as IDictionary;
                if (map == null || map.Count == 0)
                    throw new InvalidOperationException("原版配置尚未加载：" + table);
                baseline[table] = new HashSet<int>(map.Keys.Cast<int>());
            }
            var ids = (loader.activeMods ?? new List<ulong>()).Distinct().ToList();
            if (!ids.Contains(currentId)) ids.Add(currentId);
            var mods = new List<ModInput>();
            foreach (ulong id in ids)
            {
                string path = id == currentId ? currentPath : Path.Combine(ModDirectory(id), "Cfgs", "zh-cn");
                if (id != currentId && ModCfgOverridePatches.TryBuildOverlay(id, path, out string overlay, out _))
                    path = overlay;
                var mod = new ModInput { Id = id.ToString() };
                mods.Add(mod);
                if (!Directory.Exists(path)) continue;
                foreach (string file in Directory.GetFiles(path, "*Cfg.json", SearchOption.TopDirectoryOnly))
                {
                    string table = Path.GetFileNameWithoutExtension(file);
                    if (typeof(Cfg).Assembly.GetType("Config." + table) == null) continue;
                    mod.Tables.Add(table, JObject.Parse(File.ReadAllText(file)));
                }
            }
            IdRegistry saved = ReadRegistry();
            var prepared = new CollisionPlan(mods, baseline, saved);
            string cache = Path.Combine(Paths.CachePath, "EC2BUnofficialPatch", "EventCompatibility", Guid.NewGuid().ToString("N"));
            var staged = new Dictionary<string, string>();
            foreach (ModInput mod in mods)
            {
                string target = Path.Combine(cache, mod.Id, "Cfgs", "zh-cn");
                Directory.CreateDirectory(target);
                foreach (var table in prepared.Output[mod.Id])
                    File.WriteAllText(Path.Combine(target, table.Key + ".json"), table.Value.ToString(Formatting.None));
                staged.Add(mod.Id, target);
            }
            Commit(prepared);
            _baseline = baseline;
            foreach (var pair in staged) PathsByMod.Add(pair.Key, pair.Value);
            PatchLog.Info("机制模块-事件兼容计划应用：重映射=" + prepared.Report.Count(line => line.StartsWith("REMAP ")));
        }

        private static string ModDirectory(ulong id) => Sdk.PlatformAPI.Platform.Current.GetModInstallPath(id.ToString());

        private static IdRegistry ReadRegistry()
        {
            if (!File.Exists(RegistryPath) && File.Exists(RegistryPath + ".created"))
                throw new InvalidOperationException("固定 ID 映射丢失，请恢复 id-map.json；禁止为既有存档自动重分配。");
            return File.Exists(RegistryPath)
                ? JsonConvert.DeserializeObject<IdRegistry>(File.ReadAllText(RegistryPath))
                    ?? throw new InvalidOperationException("ID 映射文件为空")
                : new IdRegistry();
        }

        internal static void EnsureReloadSupported()
        {
            if (_standalone) throw new InvalidOperationException("独立事件兼容 DLL 不支持 UP CFG 热重载；请迁移到 UP 后重启。");
            if (_enabled && _plan == null) throw new InvalidOperationException("事件兼容启动计划未生效，拒绝在本局中途改变 ID 规则。");
        }

        /// <summary>Inputs are the accepted, untransformed documents, including last-good invalid-file fallbacks.</summary>
        internal static CollisionPlan PrepareReload(IList<ModInput> mods)
        {
            EnsureReloadSupported();
            if (!_enabled) return null;
            // Also detects an externally removed/corrupted registry instead of silently recreating it.
            IdRegistry disk = ReadRegistry();
            if (JsonConvert.SerializeObject(disk) != JsonConvert.SerializeObject(_plan.Registry))
                throw new InvalidOperationException("ID 映射在运行中被修改，请恢复后重启。");
            return new CollisionPlan(mods, _baseline, _plan.Registry);
        }

        internal static void Commit(CollisionPlan prepared)
        {
            if (prepared == null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(RegistryPath));
            string json = JsonConvert.SerializeObject(prepared.Registry, Formatting.Indented);
            // Marker precedes the durable write: a crash cannot silently reset existing identities.
            if (!File.Exists(RegistryPath + ".created"))
                File.WriteAllText(RegistryPath + ".created", "Keep id-map.json with saves; do not regenerate IDs for an existing playthrough.");
            if (!File.Exists(RegistryPath) || File.ReadAllText(RegistryPath) != json)
            {
                string temp = RegistryPath + ".tmp";
                File.WriteAllText(temp, json);
                if (File.Exists(RegistryPath)) File.Replace(temp, RegistryPath, RegistryPath + ".bak");
                else File.Move(temp, RegistryPath);
            }
            _plan = prepared;
        }

        internal static int Resolve(ulong modId, string table, int id) => _plan?.Resolve(modId.ToString(), table, id) ?? id;
    }
}
