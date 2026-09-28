using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Config;
using DragonBones;
using EC2BUnofficialPatch.Core;
using EC2BUnofficialPatch.Workshop;
using HarmonyLib;
using MiniGame.Badminton;
using Newtonsoft.Json.Linq;
using Sdk;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace EC2BUnofficialPatch.Features.Mechanics
{
    internal sealed class CustomBadmintonModule : IPluginModule
    {
        private static readonly IReadOnlyList<ModuleLogItem> Items = new[]
        {
            new ModuleLogItem("机制", "羽毛球模型贴图新增")
        };

        public string Key => "mechanics.custom-badminton";
        public IReadOnlyList<ModuleLogItem> LogItems => Items;

        public void Load(Harmony harmony, PluginServices services)
        {
            CustomBadmintonRegistry registry =
                CustomBadmintonRegistry.Load(services.ContentRoots.Roots);
            CustomBadmintonPatches.Initialize(registry, services.ContentRoots);

            Patch(
                harmony,
                RequireMethod(typeof(BadmintonMiniGameView), nameof(BadmintonMiniGameView.GetModelUrl), typeof(int)),
                prefix: nameof(CustomBadmintonPatches.GetModelUrlPrefix));

            Patch(
                harmony,
                AccessTools.Constructor(
                    typeof(BadmintonEnermy),
                    new[] { typeof(string), typeof(Action<BadmintonEnermy>) })
                    ?? throw new MissingMethodException(typeof(BadmintonEnermy).FullName, ".ctor"),
                prefix: nameof(CustomBadmintonPatches.EnermyConstructorPrefix));
            Patch(
                harmony,
                RequireMethod(
                    typeof(BadmintonEnermy),
                    "OnLoadObj",
                    typeof(GameObject),
                    typeof(Action<BadmintonEnermy>)),
                prefix: nameof(CustomBadmintonPatches.EnermyOnLoadObjPrefix));

            Patch(
                harmony,
                AccessTools.Constructor(
                    typeof(BadmintonPlayer),
                    new[] { typeof(string), typeof(Action<BadmintonPlayer>) })
                    ?? throw new MissingMethodException(typeof(BadmintonPlayer).FullName, ".ctor"),
                prefix: nameof(CustomBadmintonPatches.PlayerConstructorPrefix));
            Patch(
                harmony,
                RequireMethod(
                    typeof(BadmintonPlayer),
                    "OnLoadObj",
                    typeof(GameObject),
                    typeof(Action<BadmintonPlayer>)),
                prefix: nameof(CustomBadmintonPatches.PlayerOnLoadObjPrefix));

            JsonHotReloadApi.EnsureBuiltIn(
                "up.custom-badminton",
                "羽毛球模型贴图新增",
                CustomBadmintonPatches.Reload,
                250);

            PatchLog.Registration(
                "机制模块-羽毛球模型贴图新增初始化完成：" +
                $"registrations={registry.EntryCount}, variants={registry.VariantCount}");
        }

        private static void Patch(
            Harmony harmony,
            MethodBase target,
            string prefix)
        {
            harmony.Patch(
                target,
                prefix: new HarmonyMethod(typeof(CustomBadmintonPatches), prefix));
        }

        private static MethodInfo RequireMethod(Type type, string name, params Type[] parameterTypes) =>
            AccessTools.Method(type, name, parameterTypes)
            ?? throw new MissingMethodException(type.FullName, name);
    }

    internal static class CustomBadmintonPatches
    {
        private static readonly ConditionalWeakTable<BadmintonEnermy, CustomBadmintonVariant>
            PendingEnermies = new ConditionalWeakTable<BadmintonEnermy, CustomBadmintonVariant>();
        private static readonly ConditionalWeakTable<BadmintonPlayer, CustomBadmintonVariant>
            PendingPlayers = new ConditionalWeakTable<BadmintonPlayer, CustomBadmintonVariant>();

        private static CustomBadmintonRegistry _registry = CustomBadmintonRegistry.Empty;
        private static ContentRootCatalog _roots;

        internal static void Initialize(
            CustomBadmintonRegistry registry,
            ContentRootCatalog roots)
        {
            _registry = registry ?? CustomBadmintonRegistry.Empty;
            _roots = roots;
        }

        internal static JsonHotReloadResult Reload()
        {
            if (_roots == null)
                return JsonHotReloadResult.Failed("内容根目录尚未初始化");

            try
            {
                CustomBadmintonRegistry replacement =
                    CustomBadmintonRegistry.Load(_roots.Roots);
                _registry = replacement;
                return JsonHotReloadResult.Completed(
                    replacement.ConfigFileCount,
                    replacement.VariantCount,
                    "角色登记=" + replacement.EntryCount);
            }
            catch (Exception exception)
            {
                return JsonHotReloadResult.Failed(ModuleHost.GetReason(exception));
            }
        }

        public static bool GetModelUrlPrefix(int _npcId, ref string __result)
        {
            CustomBadmintonVariant playerVariant;
            bool selected = _npcId == 0
                ? _registry.TryGetNpcVariant(0, out playerVariant)
                : _registry.TryChooseOpponentVariant(_npcId, out playerVariant);
            if (!selected)
                return true;

            __result = playerVariant.Url;
            return false;
        }

        public static void EnermyConstructorPrefix(
            BadmintonEnermy __instance,
            ref string _name)
        {
            if (__instance == null || !_registry.TryGetByUrl(_name, out CustomBadmintonVariant variant))
                return;

            PendingEnermies.Remove(__instance);
            PendingEnermies.Add(__instance, variant);
            _name = variant.Prefab;
        }

        public static void EnermyOnLoadObjPrefix(
            BadmintonEnermy __instance,
            GameObject _obj)
        {
            CustomBadmintonVariant variant = null;
            if (__instance != null)
            {
                PendingEnermies.TryGetValue(__instance, out variant);
                PendingEnermies.Remove(__instance);
            }

            ApplyOrRestore(_obj, "anim_enermy", variant);
        }

        public static void PlayerConstructorPrefix(
            BadmintonPlayer __instance,
            ref string _name)
        {
            if (__instance == null || !_registry.TryGetPlayerVariant(_name, out CustomBadmintonVariant variant))
                return;

            PendingPlayers.Remove(__instance);
            PendingPlayers.Add(__instance, variant);
            _name = variant.Prefab;
        }

        public static void PlayerOnLoadObjPrefix(
            BadmintonPlayer __instance,
            GameObject _obj)
        {
            CustomBadmintonVariant variant = null;
            if (__instance != null)
            {
                PendingPlayers.TryGetValue(__instance, out variant);
                PendingPlayers.Remove(__instance);
            }

            ApplyOrRestore(_obj, "anim_player", variant);
        }

        private static void ApplyOrRestore(
            GameObject root,
            string animatorNode,
            CustomBadmintonVariant variant)
        {
            if (root == null)
                return;

            UnityEngine.Transform animator = root.transform.Find(animatorNode);
            UnityArmatureComponent component =
                animator?.Find("Armature")?.GetComponent<UnityArmatureComponent>();
            if (component == null)
            {
                if (variant != null)
                {
                    PatchLog.Warning(
                        "机制模块-羽毛球外置角色指定的 prefab 结构不兼容，已保留模板原状：" +
                        $"id={variant.Id}, prefab={variant.Prefab}, expected={animatorNode}/Armature");
                }
                return;
            }

            // Armature 本身会在外置数据与模板数据之间整体替换。状态必须挂在稳定的
            // Animator 节点上，否则替换旧 Armature 时状态也会一并销毁。
            CustomBadmintonArmatureState state =
                animator.GetComponent<CustomBadmintonArmatureState>();
            if (variant == null)
            {
                state?.RestoreOriginal();
                return;
            }

            if (state == null)
                state = animator.gameObject.AddComponent<CustomBadmintonArmatureState>();

            try
            {
                state.Apply(component, variant);
            }
            catch (Exception exception)
            {
                PatchLog.Exception(
                    "机制模块-羽毛球外置角色加载失败，已回退模板角色：" +
                    $"id={variant.Id}, url={variant.Url}, prefab={variant.Prefab}, " +
                    $"source={variant.SourcePath}",
                    exception);
                state.RestoreOriginal();
            }
        }
    }

    internal sealed class CustomBadmintonRegistry
    {
        internal const string FolderName = "custombadminton";
        internal const string FileName = "custombadmintoncfg.json";

        private readonly Dictionary<int, CustomBadmintonEntry> _entries =
            new Dictionary<int, CustomBadmintonEntry>();
        private readonly Dictionary<string, CustomBadmintonVariant> _variants =
            new Dictionary<string, CustomBadmintonVariant>(StringComparer.OrdinalIgnoreCase);

        internal static CustomBadmintonRegistry Empty { get; } = new CustomBadmintonRegistry();

        internal int EntryCount => _entries.Count;
        internal int VariantCount => _variants.Count;
        internal int ConfigFileCount { get; private set; }

        internal static CustomBadmintonRegistry Load(IReadOnlyList<ContentRoot> roots)
        {
            var registry = new CustomBadmintonRegistry();
            foreach (string configPath in EnumerateConfigFiles(roots))
            {
                registry.ConfigFileCount++;
                registry.LoadFile(configPath);
            }
            return registry;
        }

        internal bool TryGetNpcVariant(int id, out CustomBadmintonVariant variant)
        {
            variant = null;
            if (id != 0 || !_entries.TryGetValue(id, out CustomBadmintonEntry entry) ||
                entry.Variants.Count == 0)
                return false;

            if (entry.Variants.Count > 1)
            {
                bool isMale = Singleton<RoleMgr>.Ins.IsMale();
                variant = entry.Variants[isMale ? 0 : 1];
            }
            else
            {
                variant = entry.Variants[0];
            }
            return true;
        }

        internal bool TryChooseOpponentVariant(int id, out CustomBadmintonVariant variant)
        {
            variant = null;
            if (id <= 0 || !_entries.TryGetValue(id, out CustomBadmintonEntry entry))
                return false;

            List<CustomBadmintonVariant> choices = entry.Variants
                .Where(item => !IsOriginalModelUrl(item.Url))
                .ToList();
            bool hasOriginal = Cfg.BadmintonModelCfgMap != null &&
                               Cfg.BadmintonModelCfgMap.ContainsKey(id);
            int count = choices.Count + (hasOriginal ? 1 : 0);
            if (count == 0)
                return false;

            int selected = UnityEngine.Random.Range(0, count);
            if (hasOriginal && selected == 0)
                return false;

            variant = choices[selected - (hasOriginal ? 1 : 0)];
            return true;
        }

        internal bool TryGetByUrl(string url, out CustomBadmintonVariant variant)
        {
            variant = null;
            return !string.IsNullOrWhiteSpace(url) && !IsOriginalModelUrl(url) &&
                   _variants.TryGetValue(url, out variant);
        }

        private static bool IsOriginalModelUrl(string url)
        {
            if (string.Equals(url, "badminton_boy", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(url, "badminton_girl", StringComparison.OrdinalIgnoreCase))
                return true;

            if (Cfg.BadmintonModelCfgMap == null)
                return false;
            foreach (BadmintonModelCfg cfg in Cfg.BadmintonModelCfgMap.Values)
            {
                if (cfg?.url != null && cfg.url.Any(item =>
                    string.Equals(item, url, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
            return false;
        }

        internal bool TryGetPlayerVariant(
            string originalPrefab,
            out CustomBadmintonVariant variant)
        {
            variant = null;
            if (!_entries.TryGetValue(0, out CustomBadmintonEntry entry) || entry.Variants.Count == 0)
                return false;

            int index = string.Equals(
                originalPrefab,
                "badminton_boy",
                StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            if (index >= entry.Variants.Count)
                index = 0;
            variant = entry.Variants[index];
            return true;
        }

        private void LoadFile(string configPath)
        {
            JObject root;
            try
            {
                root = JObject.Parse(File.ReadAllText(configPath));
            }
            catch (Exception exception)
            {
                PatchLog.Warning(
                    $"机制模块-无法读取羽毛球外置角色配置：file={configPath}, " +
                    $"reason={ModuleHost.GetReason(exception)}");
                return;
            }

            string packageDirectory = Path.GetDirectoryName(configPath) ?? string.Empty;
            foreach (JProperty property in root.Properties())
            {
                if (!(property.Value is JObject item))
                {
                    PatchLog.Warning(
                        $"机制模块-羽毛球外置角色条目必须是对象，已忽略：key={property.Name}, source={configPath}");
                    continue;
                }

                int keyId;
                int id = item.Value<int?>("id") ??
                         (int.TryParse(property.Name, out keyId) ? keyId : -1);
                if (id < 0 ||
                    (int.TryParse(property.Name, out keyId) && keyId != id))
                {
                    PatchLog.Warning(
                        $"机制模块-羽毛球外置角色 id 非法或与对象键不一致，已忽略：" +
                        $"key={property.Name}, id={id}, source={configPath}");
                    continue;
                }

                if (id == 0)
                    LoadPlayerEntry(item, packageDirectory, configPath);
                else
                    LoadOpponentEntry(id, item, packageDirectory, configPath);
            }
        }

        private void LoadPlayerEntry(JObject item, string directory, string source)
        {
            if (_entries.TryGetValue(0, out CustomBadmintonEntry existing))
            {
                PatchLog.Warning(
                    $"机制模块-羽毛球玩家 id=0 重复注册，保留先加载的 {existing.SourcePath}，忽略 {source}");
                return;
            }

            string prefab = item.Value<string>("prefab")?.Trim();
            JArray urls = item["url"] as JArray;
            string[] names = urls?.Values<string>()
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .ToArray() ?? Array.Empty<string>();
            if (!IsSimpleName(prefab) || names.Length == 0 ||
                names.Any(name => !IsSimpleName(name)) ||
                names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length ||
                names.Any(name => _variants.ContainsKey(name)))
            {
                PatchLog.Warning($"机制模块-羽毛球玩家 url/prefab 无效或重复，已忽略：source={source}");
                return;
            }

            var variants = new List<CustomBadmintonVariant>();
            try
            {
                foreach (string name in names)
                    variants.Add(CustomBadmintonVariant.Create(0, name, prefab, directory, source));
            }
            catch (Exception exception)
            {
                PatchLog.Warning(
                    $"机制模块-羽毛球玩家资源无效，已忽略：source={source}, " +
                    $"reason={ModuleHost.GetReason(exception)}");
                return;
            }

            _entries[0] = new CustomBadmintonEntry(0, variants, source);
            foreach (CustomBadmintonVariant variant in variants)
                _variants[variant.Url] = variant;
        }

        private void LoadOpponentEntry(int id, JObject item, string directory, string source)
        {
            var outfits = new List<KeyValuePair<string, string>>();
            if (item["outfits"] != null)
            {
                if (!(item["outfits"] is JArray array))
                {
                    PatchLog.Warning($"机制模块-羽毛球对手 outfits 必须是数组：id={id}, source={source}");
                    return;
                }
                foreach (JToken token in array)
                {
                    if (token is JObject outfit)
                    {
                        outfits.Add(new KeyValuePair<string, string>(
                            outfit.Value<string>("url")?.Trim(),
                            outfit.Value<string>("prefab")?.Trim()));
                    }
                    else
                    {
                        PatchLog.Warning($"机制模块-羽毛球对手服装必须是对象，已忽略：id={id}, source={source}");
                    }
                }
            }
            else
            {
                // 旧配置的 url 数组不表示随机球服；普通 NPC 仍只使用第一项。
                string firstUrl = (item["url"] as JArray)?.Values<string>()
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
                outfits.Add(new KeyValuePair<string, string>(firstUrl, item.Value<string>("prefab")?.Trim()));
            }

            var added = new List<CustomBadmintonVariant>();
            var seenHere = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> outfit in outfits)
            {
                string url = outfit.Key;
                string prefab = outfit.Value;
                if (!IsSimpleName(url) || !IsSimpleName(prefab))
                {
                    PatchLog.Warning(
                        $"机制模块-羽毛球对手服装 url/prefab 必须是简单名称，已忽略：" +
                        $"id={id}, url={url ?? "<null>"}, source={source}");
                    continue;
                }
                if (!seenHere.Add(url) || _variants.ContainsKey(url))
                {
                    PatchLog.Warning(
                        $"机制模块-羽毛球对手服装 url 重复，已忽略：" +
                        $"id={id}, url={url}, source={source}");
                    continue;
                }
                try
                {
                    CustomBadmintonVariant variant =
                        CustomBadmintonVariant.Create(id, url, prefab, directory, source);
                    added.Add(variant);
                    _variants[url] = variant;
                }
                catch (Exception exception)
                {
                    PatchLog.Warning(
                        $"机制模块-羽毛球对手服装资源无效，已忽略：" +
                        $"id={id}, url={url}, source={source}, " +
                        $"reason={ModuleHost.GetReason(exception)}");
                }
            }

            if (added.Count == 0)
                return;
            if (_entries.TryGetValue(id, out CustomBadmintonEntry existing))
                existing.AddRange(added);
            else
                _entries[id] = new CustomBadmintonEntry(id, added, source);
        }

        private static bool IsSimpleName(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "." || value == "..")
                return false;
            return !Path.IsPathRooted(value) &&
                   value.IndexOf('/') < 0 &&
                   value.IndexOf('\\') < 0 &&
                   value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        private static IEnumerable<string> EnumerateConfigFiles(IReadOnlyList<ContentRoot> roots)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ContentRoot root in roots ?? Array.Empty<ContentRoot>())
            {
                if (root == null)
                    continue;

                string[] directories =
                {
                    Path.Combine(root.Path, "EC2BUnofficialPatch", FolderName),
                    Path.Combine(root.Path, FolderName)
                };
                foreach (string directory in directories)
                {
                    string file = Path.Combine(directory, FileName);
                    if (!File.Exists(file))
                        continue;
                    string fullPath = Path.GetFullPath(file);
                    if (seen.Add(fullPath))
                        yield return fullPath;
                }
            }
        }
    }

    internal sealed class CustomBadmintonEntry
    {
        private readonly List<CustomBadmintonVariant> _variants;

        internal CustomBadmintonEntry(
            int id,
            IReadOnlyList<CustomBadmintonVariant> variants,
            string sourcePath)
        {
            Id = id;
            _variants = new List<CustomBadmintonVariant>(variants);
            SourcePath = sourcePath;
        }

        internal int Id { get; }
        internal IReadOnlyList<CustomBadmintonVariant> Variants => _variants;
        internal string SourcePath { get; }

        internal void AddRange(IEnumerable<CustomBadmintonVariant> variants)
        {
            _variants.AddRange(variants);
        }
    }

    internal sealed class CustomBadmintonVariant
    {
        private CustomBadmintonVariant(
            int id,
            string url,
            string prefab,
            string skeletonPath,
            string atlasPath,
            string texturePath,
            string sourcePath,
            string versionKey)
        {
            Id = id;
            Url = url;
            Prefab = prefab;
            SkeletonPath = skeletonPath;
            AtlasPath = atlasPath;
            TexturePath = texturePath;
            SourcePath = sourcePath;
            VersionKey = versionKey;
        }

        internal int Id { get; }
        internal string Url { get; }
        internal string Prefab { get; }
        internal string SkeletonPath { get; }
        internal string AtlasPath { get; }
        internal string TexturePath { get; }
        internal string SourcePath { get; }
        internal string VersionKey { get; }

        internal static CustomBadmintonVariant Create(
            int id,
            string url,
            string prefab,
            string packageDirectory,
            string sourcePath)
        {
            string packageRoot = Path.GetFullPath(packageDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            string variantDirectory = Path.GetFullPath(Path.Combine(packageDirectory, url));
            string variantRoot = variantDirectory
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            if (!variantRoot.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("角色目录跳出了 custombadminton 目录");

            string skeletonPath = Path.Combine(variantDirectory, url + "_ske.json");
            string atlasPath = Path.Combine(variantDirectory, url + "_tex.json");
            if (!File.Exists(skeletonPath))
                throw new FileNotFoundException("缺少骨骼文件", skeletonPath);
            if (!File.Exists(atlasPath))
                throw new FileNotFoundException("缺少图集描述文件", atlasPath);

            JObject atlasJson = JObject.Parse(File.ReadAllText(atlasPath));
            string imagePath = atlasJson.Value<string>("imagePath")?.Trim();
            if (string.IsNullOrWhiteSpace(imagePath))
                imagePath = url + "_tex.png";
            if (Path.IsPathRooted(imagePath) || imagePath.Contains(".."))
                throw new InvalidDataException("tex.json 的 imagePath 必须位于角色目录内");

            string texturePath = Path.GetFullPath(Path.Combine(variantDirectory, imagePath));
            if (!texturePath.StartsWith(variantRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("tex.json 的 imagePath 跳出了角色目录");
            if (!string.Equals(Path.GetExtension(texturePath), ".png", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("羽毛球图集当前只支持 PNG");
            if (!File.Exists(texturePath))
                throw new FileNotFoundException("缺少 tex.json 指定的贴图", texturePath);

            // 启动时先验证两份 JSON 语法，避免进入小游戏后才发现损坏文件。
            JObject.Parse(File.ReadAllText(skeletonPath));

            string versionKey = string.Join("|", new[]
            {
                DescribeFile(skeletonPath),
                DescribeFile(atlasPath),
                DescribeFile(texturePath)
            });
            return new CustomBadmintonVariant(
                id,
                url,
                prefab,
                skeletonPath,
                atlasPath,
                texturePath,
                sourcePath,
                versionKey);
        }

        private static string DescribeFile(string path)
        {
            var info = new FileInfo(path);
            return info.Length + ":" + info.LastWriteTimeUtc.Ticks;
        }
    }

    internal sealed class CustomBadmintonArmatureState : MonoBehaviour
    {
        private static long _nextDataId;

        private UnityArmatureComponent _component;
        private UnityDragonBonesData _originalData;
        private ArmatureSettings _originalSettings;
        private ArmatureHostSettings _hostSettings;
        private CustomBadmintonRuntimeData _customData;
        private string _activeVersionKey;

        internal void Apply(
            UnityArmatureComponent component,
            CustomBadmintonVariant variant)
        {
            if (component == null)
                throw new ArgumentNullException(nameof(component));
            if (variant == null)
                throw new ArgumentNullException(nameof(variant));

            if (_component == null)
            {
                _component = component;
                _originalData = component.unityData;
                _originalSettings = ArmatureSettings.Capture(component);
                _hostSettings = ArmatureHostSettings.Capture(component.gameObject);
            }

            if (_customData != null &&
                string.Equals(_activeVersionKey, variant.VersionKey, StringComparison.Ordinal))
            {
                return;
            }

            CustomBadmintonRuntimeData replacement =
                CustomBadmintonRuntimeData.Create(
                    _originalData,
                    variant,
                    Interlocked.Increment(ref _nextDataId));
            try
            {
                ArmatureSettings settings = ArmatureSettings.Capture(_component);
                UnityArmatureComponent previous = _component;
                UnityArmatureComponent rebuilt = BuildFresh(replacement.Data, settings);
                ActivateReplacement(previous, rebuilt);

                CustomBadmintonRuntimeData previousData = _customData;
                _component = rebuilt;
                _customData = replacement;
                _activeVersionKey = variant.VersionKey;
                replacement = null;
                Retire(previous);
                previousData?.Dispose(true);
                PatchLog.Info(
                    "机制模块-羽毛球外置角色已应用：" +
                    $"id={variant.Id}, url={variant.Url}, prefab={variant.Prefab}, " +
                    $"texture={variant.TexturePath}");
            }
            catch
            {
                replacement?.Dispose(false);
                throw;
            }
        }

        internal void RestoreOriginal()
        {
            if (_component == null || _customData == null)
                return;

            try
            {
                UnityArmatureComponent previous = _component;
                UnityArmatureComponent rebuilt = BuildFresh(
                    _originalData,
                    _originalSettings ?? ArmatureSettings.Capture(previous));
                ActivateReplacement(previous, rebuilt);

                CustomBadmintonRuntimeData previousData = _customData;
                _component = rebuilt;
                _customData = null;
                _activeVersionKey = null;
                Retire(previous);
                previousData?.Dispose(true);
            }
            catch (Exception exception)
            {
                PatchLog.Exception("机制模块-羽毛球模板角色恢复失败", exception);
            }
        }

        private UnityArmatureComponent BuildFresh(
            UnityDragonBonesData data,
            ArmatureSettings settings)
        {
            if (data == null || data.dragonBonesJSON == null || data.textureAtlas == null)
                throw new InvalidDataException("DragonBones 数据不完整");
            if (settings == null || string.IsNullOrWhiteSpace(settings.ArmatureName))
                throw new InvalidDataException("模板 prefab 没有 armatureName");

            if (UnityFactory.factory.LoadData(data, settings.IsUGUI) == null)
                throw new InvalidDataException("DragonBones 无法解析骨骼数据");

            GameObject host = _hostSettings.CreateDetached();
            try
            {
                UnityArmatureComponent rebuilt = UnityFactory.factory.BuildArmatureComponent(
                    settings.ArmatureName,
                    data.dataName,
                    null,
                    null,
                    host,
                    settings.IsUGUI);
                if (rebuilt?.armature == null)
                    throw new InvalidDataException(
                        "骨骼数据中找不到模板需要的 Armature：" + settings.ArmatureName);

                rebuilt.unityData = data;
                settings.RestoreComponent(rebuilt);
                settings.RestoreArmature(rebuilt);
                ValidateDisplays(rebuilt);
                return rebuilt;
            }
            catch
            {
                UnityArmatureComponent failed = host.GetComponent<UnityArmatureComponent>();
                if (failed?.armature != null)
                {
                    failed.Dispose(false);
                    UnityFactory.factory._dragonBones.AdvanceTime(0f);
                }
                UnityObject.Destroy(host);
                throw;
            }
        }

        private void ActivateReplacement(
            UnityArmatureComponent previous,
            UnityArmatureComponent replacement)
        {
            if (previous == null || replacement == null)
                throw new InvalidOperationException("Armature 替换对象无效");

            GameObject oldHost = previous.gameObject;
            oldHost.name = "Armature__EC2B_Retired";
            oldHost.SetActive(false);
            _hostSettings.Attach(replacement.gameObject);
        }

        private static void Retire(UnityArmatureComponent component)
        {
            if (component == null)
                return;

            GameObject host = component.gameObject;
            host.SetActive(false);
            if (component.armature != null)
            {
                component.Dispose(false);
                UnityFactory.factory._dragonBones.AdvanceTime(0f);
            }
            UnityObject.Destroy(host);
        }

        private static void ValidateDisplays(UnityArmatureComponent component)
        {
            foreach (Slot slot in component.armature.GetSlots())
            {
                UnitySlot unitySlot = slot as UnitySlot;
                if (unitySlot == null || unitySlot.renderDisplay == null)
                    throw new InvalidDataException(
                        "DragonBones 插槽没有可显示对象：" + (slot?.name ?? "<unknown>"));
            }

            string[] requiredAnimations =
            {
                "idle", "hit", "hit2", "hit3", "jump", "move", "start"
            };
            foreach (string animation in requiredAnimations)
            {
                if (!component.animation.HasAnimation(animation))
                    throw new InvalidDataException("DragonBones 缺少羽毛球动画：" + animation);
            }
        }

        private sealed class ArmatureSettings
        {
            private string _armatureName;
            private string _animationName;
            private bool _isUGUI;
            private bool _debugDraw;
            private SortingMode _sortingMode;
            private string _sortingLayerName;
            private int _sortingOrder;
            private float _zSpace;
            private bool _flipX;
            private bool _flipY;
            private float _timeScale = 1f;

            internal string ArmatureName => _armatureName;
            internal bool IsUGUI => _isUGUI;

            internal static ArmatureSettings Capture(UnityArmatureComponent component)
            {
                if (component == null)
                    return new ArmatureSettings();

                var settings = new ArmatureSettings
                {
                    _armatureName = component.armatureName,
                    _animationName = component.animationName,
                    _isUGUI = component.isUGUI,
                    _debugDraw = component.debugDraw,
                    _sortingMode = component.sortingMode,
                    _sortingLayerName = component.sortingLayerName,
                    _sortingOrder = component.sortingOrder,
                    _zSpace = component.zSpace
                };
                if (component.armature != null)
                {
                    settings._flipX = component.armature.flipX;
                    settings._flipY = component.armature.flipY;
                    if (component.animation != null)
                        settings._timeScale = component.animation.timeScale;
                }
                return settings;
            }

            internal void RestoreComponent(UnityArmatureComponent component)
            {
                if (component == null)
                    return;
                component.armatureName = _armatureName;
                component.animationName = _animationName;
                component.isUGUI = _isUGUI;
                component.debugDraw = _debugDraw;
                component.sortingMode = _sortingMode;
                component.sortingLayerName = _sortingLayerName;
                component.sortingOrder = _sortingOrder;
                component.zSpace = _zSpace;
            }

            internal void RestoreArmature(UnityArmatureComponent component)
            {
                if (component?.armature == null)
                    return;
                component.armature.flipX = _flipX;
                component.armature.flipY = _flipY;
                if (component.animation != null)
                    component.animation.timeScale = _timeScale;
            }
        }

        private sealed class ArmatureHostSettings
        {
            private UnityEngine.Transform _parent;
            private int _siblingIndex;
            private int _layer;
            private HideFlags _hideFlags;
            private bool _activeSelf;
            private Vector3 _localPosition;
            private Quaternion _localRotation;
            private Vector3 _localScale;
            private Vector2 _anchorMin;
            private Vector2 _anchorMax;
            private Vector2 _sizeDelta;
            private Vector2 _pivot;
            private Vector3 _anchoredPosition3D;

            internal static ArmatureHostSettings Capture(GameObject host)
            {
                if (host == null)
                    throw new ArgumentNullException(nameof(host));

                UnityEngine.Transform transform = host.transform;
                var result = new ArmatureHostSettings
                {
                    _parent = transform.parent,
                    _siblingIndex = transform.GetSiblingIndex(),
                    _layer = host.layer,
                    _hideFlags = host.hideFlags,
                    _activeSelf = host.activeSelf,
                    _localPosition = transform.localPosition,
                    _localRotation = transform.localRotation,
                    _localScale = transform.localScale
                };
                RectTransform rect = transform as RectTransform;
                if (rect != null)
                {
                    result._anchorMin = rect.anchorMin;
                    result._anchorMax = rect.anchorMax;
                    result._sizeDelta = rect.sizeDelta;
                    result._pivot = rect.pivot;
                    result._anchoredPosition3D = rect.anchoredPosition3D;
                }
                return result;
            }

            internal GameObject CreateDetached()
            {
                var host = new GameObject("Armature__EC2B_New", typeof(RectTransform));
                host.SetActive(false);
                host.layer = _layer;
                host.hideFlags = _hideFlags;
                return host;
            }

            internal void Attach(GameObject host)
            {
                if (host == null)
                    throw new ArgumentNullException(nameof(host));

                RectTransform rect = host.transform as RectTransform;
                rect.SetParent(_parent, false);
                rect.localPosition = _localPosition;
                rect.localRotation = _localRotation;
                rect.localScale = _localScale;
                rect.anchorMin = _anchorMin;
                rect.anchorMax = _anchorMax;
                rect.sizeDelta = _sizeDelta;
                rect.pivot = _pivot;
                rect.anchoredPosition3D = _anchoredPosition3D;
                rect.SetSiblingIndex(_siblingIndex);
                host.name = "Armature";
                host.SetActive(_activeSelf);
            }
        }

        private void OnDestroy()
        {
            if (_customData != null)
            {
                _customData.Dispose(false);
                _customData = null;
            }
            _component = null;
            _originalData = null;
            _originalSettings = null;
            _hostSettings = null;
        }
    }

    internal sealed class CustomBadmintonRuntimeData : IDisposable
    {
        private readonly List<UnityObject> _ownedObjects;

        private CustomBadmintonRuntimeData(
            UnityDragonBonesData data,
            List<UnityObject> ownedObjects)
        {
            Data = data;
            _ownedObjects = ownedObjects;
        }

        internal UnityDragonBonesData Data { get; }

        internal static CustomBadmintonRuntimeData Create(
            UnityDragonBonesData source,
            CustomBadmintonVariant variant,
            long sequence)
        {
            UnityDragonBonesData.TextureAtlas sourceAtlas =
                source?.textureAtlas?.FirstOrDefault(item => item != null);
            if (sourceAtlas == null)
                throw new InvalidDataException("模板 prefab 没有可复用的 DragonBones 图集材质");

            var owned = new List<UnityObject>();
            try
            {
                string dataName = "EC2B_CustomBadminton_" + sequence;
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    name = dataName,
                    hideFlags = HideFlags.HideAndDontSave
                };
                owned.Add(texture);
                if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(variant.TexturePath), false))
                    throw new InvalidDataException("Unity 无法解码 PNG 贴图");
                if (sourceAtlas.texture != null)
                {
                    texture.filterMode = sourceAtlas.texture.filterMode;
                    texture.wrapMode = sourceAtlas.texture.wrapMode;
                    texture.anisoLevel = sourceAtlas.texture.anisoLevel;
                }

                TextAsset skeleton = new TextAsset(File.ReadAllText(variant.SkeletonPath))
                {
                    name = variant.Url + "_ske",
                    hideFlags = HideFlags.HideAndDontSave
                };
                TextAsset atlasJson = new TextAsset(File.ReadAllText(variant.AtlasPath))
                {
                    name = variant.Url + "_tex",
                    hideFlags = HideFlags.HideAndDontSave
                };
                owned.Add(skeleton);
                owned.Add(atlasJson);

                Material material = CreateMaterial(
                    sourceAtlas.material,
                    "Sprites/Default",
                    dataName + "_Mat",
                    texture);
                Material uiMaterial = CreateMaterial(
                    sourceAtlas.uiMaterial,
                    "UI/Default",
                    dataName + "_UI_Mat",
                    texture);
                owned.Add(material);
                owned.Add(uiMaterial);

                UnityDragonBonesData data = ScriptableObject.CreateInstance<UnityDragonBonesData>();
                data.name = dataName;
                data.hideFlags = HideFlags.HideAndDontSave;
                data.dataName = dataName;
                data.dragonBonesJSON = skeleton;
                data.textureAtlas = new[]
                {
                    new UnityDragonBonesData.TextureAtlas
                    {
                        textureAtlasJSON = atlasJson,
                        texture = texture,
                        material = material,
                        uiMaterial = uiMaterial
                    }
                };
                owned.Add(data);
                return new CustomBadmintonRuntimeData(data, owned);
            }
            catch
            {
                DestroyOwned(owned);
                throw;
            }
        }

        public void Dispose() => Dispose(true);

        internal void Dispose(bool disposeFactoryData)
        {
            if (Data != null)
            {
                try
                {
                    Data.RemoveFromFactory(disposeFactoryData);
                }
                catch (Exception exception)
                {
                    PatchLog.Warning(
                        "机制模块-羽毛球外置 DragonBones 缓存清理失败：" +
                        ModuleHost.GetReason(exception));
                }
            }
            DestroyOwned(_ownedObjects);
            _ownedObjects.Clear();
        }

        private static Material CreateMaterial(
            Material source,
            string fallbackShader,
            string name,
            Texture2D texture)
        {
            Material material;
            if (source != null)
            {
                material = new Material(source);
            }
            else
            {
                Shader shader = Shader.Find(fallbackShader);
                if (shader == null)
                    throw new InvalidDataException("找不到 Unity Shader：" + fallbackShader);
                material = new Material(shader);
            }

            material.name = name;
            material.hideFlags = HideFlags.HideAndDontSave;
            material.mainTexture = texture;
            return material;
        }

        private static void DestroyOwned(IEnumerable<UnityObject> objects)
        {
            foreach (UnityObject item in objects.Reverse())
            {
                if (item != null)
                    UnityObject.Destroy(item);
            }
        }
    }
}
