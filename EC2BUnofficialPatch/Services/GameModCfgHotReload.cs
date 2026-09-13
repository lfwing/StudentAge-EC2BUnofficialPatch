using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Config;
using EC2BUnofficialPatch.Core;
using EC2BUnofficialPatch.Features.Mechanics;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EC2BUnofficialPatch.Services
{
    /// <summary>
    /// 所有已启用 Mod 的原版类型 *Cfg.json 热重载。
    /// 每个文件独立保留上一次有效文档；单个坏文件不会阻断其他 Mod，最终 CfgMap 仍事务替换。
    /// </summary>
    internal static class GameModCfgHotReload
    {
        private static readonly object SyncRoot = new object();
        private static readonly FieldInfo CfgMapsField = AccessTools.Field(typeof(ModCtrl), "cfgMaps");
        private static readonly List<ModCfgSource> Sources = new List<ModCfgSource>();
        private static readonly HashSet<string> SourceKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, Dictionary<int, object>> _baseMaps;
        private static Dictionary<int, List<int>> _baseMapFloors;
        private static Dictionary<string, HashSet<int>> _appliedModIds;
        private static Dictionary<string, AcceptedCfgFile> _acceptedFiles;
        private static Dictionary<string, string> _fileFingerprints;
        private static List<string> _orderedFileKeys;
        private static ModCtrl _owner;

        internal static void RecordSource(ulong modId, string originalCfgPath)
        {
            if (string.IsNullOrWhiteSpace(originalCfgPath) || !Path.IsPathRooted(originalCfgPath)) return;
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(originalCfgPath).TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch { return; }

            string key = modId + "|" + fullPath;
            lock (SyncRoot)
            {
                if (SourceKeys.Add(key)) Sources.Add(new ModCfgSource(modId, fullPath));
            }
        }

        public static void CaptureBaseBeforeModMerge(ModCtrl __instance)
        {
            if (__instance == null || CfgMapsField == null) return;
            lock (SyncRoot)
            {
                if (_baseMaps != null) return;
                _owner = __instance;
                _baseMaps = CaptureAllCfgMaps();
                _baseMapFloors = CaptureBaseMapFloors(_baseMaps);
                _appliedModIds = ReadRawCfgMaps(__instance).ToDictionary(
                    pair => NormalizeConfigName(pair.Key),
                    pair => new HashSet<int>(pair.Value.Keys),
                    StringComparer.Ordinal);
                _acceptedFiles = new Dictionary<string, AcceptedCfgFile>(StringComparer.OrdinalIgnoreCase);
                _fileFingerprints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    List<CfgFileDescriptor> files = DiscoverFiles();
                    _orderedFileKeys = files.Select(file => file.Key).ToList();
                    foreach (CfgFileDescriptor file in files)
                    {
                        _fileFingerprints[file.Key] = file.Fingerprint;
                        try { _acceptedFiles[file.Key] = ParseFile(file); }
                        catch { /* 原版已处理启动错误；这里只建立有效基线。 */ }
                    }
                    PatchLog.Info(
                        "优化模块-.json热重载已捕获原版 CFG 基线：" +
                        $"maps={_baseMaps.Count}, modTypes={_appliedModIds.Count}, " +
                        $"enabledMods={Sources.Count}, cfgFiles={files.Count}");
                }
                catch (Exception exception)
                {
                    PatchLog.Warning("优化模块-.json热重载建立普通 Mod CFG 文件基线失败：" + DescribeError(exception));
                }
            }
        }

        internal static JsonHotReloadResult Reload()
        {
            lock (SyncRoot)
            {
                if (_baseMaps == null || _owner == null)
                    return JsonHotReloadResult.Skip("游戏尚未完成 Mod CFG 初始化");
                if (Sources.Count == 0)
                    return JsonHotReloadResult.Skip("当前没有已启用 Mod 的 CFG 来源");
                try
                {
                    List<CfgFileDescriptor> orderedFiles = DiscoverFiles();
                    Dictionary<string, CfgFileDescriptor> current = orderedFiles.ToDictionary(
                        file => file.Key, StringComparer.OrdinalIgnoreCase);
                    string[] changedKeys = FindChangedKeys(current);
                    if (changedKeys.Length == 0)
                        return JsonHotReloadResult.Skip("普通 Mod CFG 未变化");

                    var candidate = new Dictionary<string, AcceptedCfgFile>(
                        _acceptedFiles ?? new Dictionary<string, AcceptedCfgFile>(),
                        StringComparer.OrdinalIgnoreCase);
                    var changes = new List<CfgFileChange>();
                    int parseErrors = 0;
                    foreach (string key in changedKeys)
                    {
                        candidate.TryGetValue(key, out AcceptedCfgFile previous);
                        if (!current.TryGetValue(key, out CfgFileDescriptor descriptor))
                        {
                            candidate.Remove(key);
                            CfgFileChange deleted = Compare(previous, null);
                            if (deleted.HasChanges) changes.Add(deleted);
                            continue;
                        }

                        try
                        {
                            AcceptedCfgFile parsed = ParseFile(descriptor);
                            candidate[key] = parsed;
                            CfgFileChange change = Compare(previous, parsed);
                            if (change.HasChanges) changes.Add(change);
                        }
                        catch (Exception exception)
                        {
                            parseErrors++;
                            PatchLog.Error(
                                "优化模块-普通 Mod CFG 热重载文件失败，已保留该文件上一次有效注册：" +
                                $"mod={descriptor.ModId}, type={descriptor.ConfigName}, " +
                                $"file={descriptor.DisplayPath}, reason={DescribeError(exception)}");
                        }
                    }

                    Dictionary<string, string> nextFingerprints = current.ToDictionary(
                        pair => pair.Key, pair => pair.Value.Fingerprint, StringComparer.OrdinalIgnoreCase);
                    if (changes.Count == 0)
                    {
                        _acceptedFiles = candidate;
                        _fileFingerprints = nextFingerprints;
                        return JsonHotReloadResult.Skip(parseErrors > 0
                            ? "只有无效变更，已保留旧注册"
                            : "文件内容语义未变化");
                    }

                    ReloadPlan plan = BuildPlan(orderedFiles, candidate);
                    ApplyPlan(plan);
                    if (PluginConfig.MapFloorCompatibility?.Value == true ||
                        PluginConfig.MapFloorHierarchyExtension?.Value == true)
                        TryRebuildMapFloors(orderedFiles, candidate, "热重载");
                    _acceptedFiles = candidate;
                    _fileFingerprints = nextFingerprints;
                    _orderedFileKeys = orderedFiles.Select(file => file.Key).ToList();
                    foreach (CfgFileChange change in changes)
                    {
                        PatchLog.Info(
                            "优化模块-普通 Mod CFG 已热重载：" +
                            $"mod={change.ModId}, type={change.ConfigName}, " +
                            $"新增={FormatIds(change.Added)}, 修改={FormatIds(change.Modified)}, " +
                            $"删除={FormatIds(change.Removed)}, file={change.DisplayPath}");
                    }
                    int changedEntries = changes.Sum(change =>
                        change.Added.Count + change.Modified.Count + change.Removed.Count);
                    return JsonHotReloadResult.Completed(
                        changes.Count, changedEntries,
                        $"变更文件={changes.Count}, 启用Mod={Sources.Count}, 解析错误={parseErrors}");
                }
                catch (Exception exception)
                {
                    return JsonHotReloadResult.Failed(DescribeError(exception));
                }
            }
        }

        internal static MapFloorRebuildResult RebuildMapFloors()
        {
            lock (SyncRoot)
            {
                if (_baseMaps == null)
                    return MapFloorRebuildResult.Empty;
                return RebuildMapFloors(_orderedFileKeys, _acceptedFiles);
            }
        }

        private static void TryRebuildMapFloors(
            IReadOnlyList<CfgFileDescriptor> orderedFiles,
            IReadOnlyDictionary<string, AcceptedCfgFile> accepted,
            string phase)
        {
            try
            {
                RebuildMapFloors(
                    orderedFiles?.Select(file => file.Key).ToArray(),
                    accepted);
            }
            catch (Exception exception)
            {
                PatchLog.Error(
                    $"优化模块-地图子地点兼容{phase}重建失败，已保留重建前地图数据：" +
                    DescribeError(exception));
            }
        }

        private static MapFloorRebuildResult RebuildMapFloors(
            IReadOnlyList<string> orderedFileKeys,
            IReadOnlyDictionary<string, AcceptedCfgFile> accepted)
        {
            var documents = new List<MapFloorCfgDocument>();
            foreach (string key in orderedFileKeys ?? Array.Empty<string>())
            {
                if (accepted == null ||
                    !accepted.TryGetValue(key, out AcceptedCfgFile file) ||
                    !string.Equals(file.ConfigName, "MapCfg", StringComparison.Ordinal))
                {
                    continue;
                }

                var entries = new List<MapFloorCfgEntry>();
                foreach (KeyValuePair<int, AcceptedCfgEntry> pair in file.Entries)
                    entries.Add(new MapFloorCfgEntry(pair.Key, ReadFloors(pair.Value.Raw)));
                documents.Add(new MapFloorCfgDocument(file.ModId, entries));
            }

            return MapFloorCompatibilityService.Rebuild(
                _baseMapFloors,
                documents,
                PluginConfig.MapFloorCompatibility?.Value == true,
                PluginConfig.MapFloorHierarchyExtension?.Value == true);
        }

        private static IReadOnlyList<int> ReadFloors(JToken raw)
        {
            JToken floors = (raw as JObject)?["floors"];
            if (floors == null || floors.Type == JTokenType.Null)
                return null;
            return floors.ToObject<List<int>>();
        }

        private static List<CfgFileDescriptor> DiscoverFiles()
        {
            var result = new List<CfgFileDescriptor>();
            foreach (ModCfgSource source in Sources)
            {
                string effectivePath = source.OriginalPath;
                var enhancedSources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (ModCfgOverridePatches.TryBuildOverlay(
                    source.ModId, source.OriginalPath,
                    out string overlayPath, out List<CfgOverlayDecision> decisions))
                {
                    effectivePath = overlayPath;
                    foreach (CfgOverlayDecision decision in decisions)
                        enhancedSources[decision.CanonicalFileName] = decision.SourcePath;
                }
                if (!Directory.Exists(effectivePath)) continue;

                foreach (string effectiveFile in Directory.GetFiles(
                    effectivePath, "*Cfg.json", SearchOption.TopDirectoryOnly))
                {
                    string fileName = Path.GetFileName(effectiveFile);
                    string configName = Path.GetFileNameWithoutExtension(effectiveFile);
                    Type configType = typeof(Cfg).Assembly.GetType("Config." + configName, false, false);
                    if (configType == null || ResolveCfgMapProperty(configName) == null) continue;
                    string displayPath = enhancedSources.TryGetValue(fileName, out string enhanced)
                        ? enhanced : Path.Combine(source.OriginalPath, fileName);
                    string content;
                    try { content = File.ReadAllText(effectiveFile); }
                    catch (Exception exception)
                    {
                        throw new IOException(
                            $"无法读取普通 Mod CFG：file={displayPath}, reason={exception.Message}", exception);
                    }
                    result.Add(new CfgFileDescriptor(
                        source.ModId, configName, configType, displayPath, content));
                }
            }
            return result;
        }

        private static AcceptedCfgFile ParseFile(CfgFileDescriptor descriptor)
        {
            JObject document;
            try { document = JObject.Parse(descriptor.Content); }
            catch (Exception exception)
            {
                throw new InvalidDataException(
                    $"JSON 解析失败：file={descriptor.DisplayPath}, reason={ModuleHost.GetReason(exception)}",
                    exception);
            }

            var entries = new Dictionary<int, AcceptedCfgEntry>();
            JsonSerializer serializer = JsonSerializer.CreateDefault();
            foreach (JProperty property in document.Properties())
            {
                if (!int.TryParse(property.Name, out int id))
                    throw new InvalidDataException(
                        $"CFG 条目 ID 不是整数：file={descriptor.DisplayPath}, id={property.Name}");
                object typed;
                try { typed = property.Value.ToObject(descriptor.ConfigType, serializer); }
                catch (Exception exception)
                {
                    throw new InvalidDataException(
                        $"CFG 条目反序列化失败：file={descriptor.DisplayPath}, id={id}, " +
                        $"type={descriptor.ConfigType.FullName}, reason={ModuleHost.GetReason(exception)}", exception);
                }
                if (typed == null)
                    throw new InvalidDataException($"CFG 条目为空：file={descriptor.DisplayPath}, id={id}");
                entries[id] = new AcceptedCfgEntry(property.Value.DeepClone(), typed);
            }
            return new AcceptedCfgFile(
                descriptor.ModId, descriptor.ConfigName, descriptor.DisplayPath, entries);
        }

        private static string[] FindChangedKeys(IReadOnlyDictionary<string, CfgFileDescriptor> current)
        {
            Dictionary<string, string> previous = _fileFingerprints ??
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return current.Keys
                .Where(key => !previous.TryGetValue(key, out string fingerprint) ||
                              fingerprint != current[key].Fingerprint)
                .Concat(previous.Keys.Where(key => !current.ContainsKey(key)))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static CfgFileChange Compare(AcceptedCfgFile previous, AcceptedCfgFile current)
        {
            AcceptedCfgFile identity = current ?? previous;
            if (identity == null) return CfgFileChange.Empty;
            IReadOnlyDictionary<int, AcceptedCfgEntry> oldEntries = previous?.Entries ??
                new Dictionary<int, AcceptedCfgEntry>();
            IReadOnlyDictionary<int, AcceptedCfgEntry> newEntries = current?.Entries ??
                new Dictionary<int, AcceptedCfgEntry>();
            int[] added = newEntries.Keys.Except(oldEntries.Keys).OrderBy(id => id).ToArray();
            int[] removed = oldEntries.Keys.Except(newEntries.Keys).OrderBy(id => id).ToArray();
            int[] modified = newEntries.Keys.Intersect(oldEntries.Keys)
                .Where(id => !JToken.DeepEquals(oldEntries[id].Raw, newEntries[id].Raw))
                .OrderBy(id => id).ToArray();
            return new CfgFileChange(
                identity.ModId, identity.ConfigName, identity.DisplayPath,
                added, modified, removed);
        }

        private static ReloadPlan BuildPlan(
            IReadOnlyList<CfgFileDescriptor> orderedFiles,
            IReadOnlyDictionary<string, AcceptedCfgFile> accepted)
        {
            var rawMaps = new Dictionary<string, Dictionary<int, object>>(StringComparer.Ordinal);
            var typedMaps = new Dictionary<string, Dictionary<int, object>>(StringComparer.Ordinal);
            foreach (CfgFileDescriptor descriptor in orderedFiles)
            {
                if (!accepted.TryGetValue(descriptor.Key, out AcceptedCfgFile file)) continue;
                if (!rawMaps.TryGetValue(file.ConfigName, out Dictionary<int, object> raw))
                {
                    raw = new Dictionary<int, object>();
                    rawMaps[file.ConfigName] = raw;
                    typedMaps[file.ConfigName] = new Dictionary<int, object>();
                }
                Dictionary<int, object> typed = typedMaps[file.ConfigName];
                foreach (KeyValuePair<int, AcceptedCfgEntry> pair in file.Entries)
                {
                    if (raw.ContainsKey(pair.Key)) continue;
                    raw[pair.Key] = pair.Value.Raw.DeepClone();
                    typed[pair.Key] = pair.Value.Typed;
                }
            }
            return new ReloadPlan(rawMaps, typedMaps);
        }

        private static void ApplyPlan(ReloadPlan plan)
        {
            HashSet<string> affected = new HashSet<string>(
                _appliedModIds?.Keys ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            affected.UnionWith(plan.TypedMaps.Keys);
            var rollback = new Dictionary<string, Dictionary<int, object>>(StringComparer.Ordinal);
            Dictionary<string, Dictionary<int, object>> liveRaw = ReadRawCfgMaps(_owner);
            Dictionary<string, Dictionary<int, object>> rawRollback = liveRaw.ToDictionary(
                pair => pair.Key, pair => new Dictionary<int, object>(pair.Value), StringComparer.Ordinal);
            try
            {
                foreach (string configName in affected)
                {
                    IDictionary map = GetCfgMap(configName);
                    if (map == null) throw new MissingMemberException("找不到运行时 CFG Map：" + configName + "Map");
                    rollback[configName] = CloneMap(map);
                    if (_appliedModIds != null &&
                        _appliedModIds.TryGetValue(configName, out HashSet<int> oldIds))
                    {
                        _baseMaps.TryGetValue(configName, out Dictionary<int, object> baseline);
                        foreach (int id in oldIds)
                        {
                            if (baseline != null && baseline.TryGetValue(id, out object baseValue)) map[id] = baseValue;
                            else map.Remove(id);
                        }
                    }
                    if (plan.TypedMaps.TryGetValue(configName, out Dictionary<int, object> replacements))
                    {
                        foreach (KeyValuePair<int, object> pair in replacements) map[pair.Key] = pair.Value;
                    }
                }
                liveRaw.Clear();
                foreach (KeyValuePair<string, Dictionary<int, object>> pair in plan.RawMaps)
                    liveRaw["Config." + pair.Key] = pair.Value;
                _appliedModIds = plan.TypedMaps.ToDictionary(
                    pair => pair.Key, pair => new HashSet<int>(pair.Value.Keys), StringComparer.Ordinal);
            }
            catch
            {
                foreach (KeyValuePair<string, Dictionary<int, object>> item in rollback)
                {
                    IDictionary map = GetCfgMap(item.Key);
                    if (map == null) continue;
                    map.Clear();
                    foreach (KeyValuePair<int, object> pair in item.Value) map[pair.Key] = pair.Value;
                }
                liveRaw.Clear();
                foreach (KeyValuePair<string, Dictionary<int, object>> pair in rawRollback)
                    liveRaw[pair.Key] = pair.Value;
                throw;
            }
        }

        private static Dictionary<string, Dictionary<int, object>> CaptureAllCfgMaps()
        {
            var result = new Dictionary<string, Dictionary<int, object>>(StringComparer.Ordinal);
            foreach (PropertyInfo property in typeof(Cfg).GetProperties(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!property.Name.EndsWith("CfgMap", StringComparison.Ordinal) ||
                    !typeof(IDictionary).IsAssignableFrom(property.PropertyType)) continue;
                if (!(property.GetValue(null, null) is IDictionary map)) continue;
                result[property.Name.Substring(0, property.Name.Length - 3)] = CloneMap(map);
            }
            return result;
        }

        private static Dictionary<int, List<int>> CaptureBaseMapFloors(
            IReadOnlyDictionary<string, Dictionary<int, object>> maps)
        {
            var result = new Dictionary<int, List<int>>();
            if (maps == null ||
                !maps.TryGetValue("MapCfg", out Dictionary<int, object> mapCfgs))
            {
                return result;
            }

            foreach (KeyValuePair<int, object> pair in mapCfgs)
            {
                if (pair.Value is MapCfg map)
                    result[pair.Key] = map.floors == null ? null : new List<int>(map.floors);
            }
            return result;
        }

        private static PropertyInfo ResolveCfgMapProperty(string configName) => typeof(Cfg).GetProperty(
            configName + "Map", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        private static IDictionary GetCfgMap(string configName) =>
            ResolveCfgMapProperty(configName)?.GetValue(null, null) as IDictionary;
        private static Dictionary<int, object> CloneMap(IDictionary map)
        {
            var clone = new Dictionary<int, object>();
            foreach (DictionaryEntry item in map) if (item.Key is int id) clone[id] = item.Value;
            return clone;
        }
        private static Dictionary<string, Dictionary<int, object>> ReadRawCfgMaps(ModCtrl owner) =>
            CfgMapsField?.GetValue(owner) as Dictionary<string, Dictionary<int, object>>
            ?? throw new MissingMemberException("ModCtrl.cfgMaps 不存在或类型已变化");
        private static string NormalizeConfigName(string typeName) =>
            typeName != null && typeName.StartsWith("Config.", StringComparison.Ordinal)
                ? typeName.Substring("Config.".Length) : typeName ?? string.Empty;
        private static string Hash(string content)
        {
            using (SHA256 sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(content ?? string.Empty)));
        }
        private static string DescribeError(Exception exception)
        {
            Exception current = exception is TargetInvocationException && exception.InnerException != null
                ? exception.InnerException : exception;
            return string.IsNullOrWhiteSpace(current.Message)
                ? current.GetType().Name : current.Message.Replace(Environment.NewLine, " ");
        }
        private static string FormatIds(IReadOnlyList<int> ids)
        {
            if (ids == null || ids.Count == 0) return "0";
            string shown = string.Join(",", ids.Take(12));
            return ids.Count > 12 ? $"{ids.Count}[{shown},…]" : $"{ids.Count}[{shown}]";
        }

        private sealed class ModCfgSource
        {
            internal ModCfgSource(ulong modId, string originalPath) { ModId = modId; OriginalPath = originalPath; }
            internal ulong ModId { get; }
            internal string OriginalPath { get; }
        }
        private sealed class CfgFileDescriptor
        {
            internal CfgFileDescriptor(ulong modId, string configName, Type configType, string displayPath, string content)
            {
                ModId = modId; ConfigName = configName; ConfigType = configType;
                DisplayPath = displayPath; Content = content; Fingerprint = Hash(content);
                Key = modId + "|" + configName;
            }
            internal string Key { get; }
            internal ulong ModId { get; }
            internal string ConfigName { get; }
            internal Type ConfigType { get; }
            internal string DisplayPath { get; }
            internal string Content { get; }
            internal string Fingerprint { get; }
        }
        private sealed class AcceptedCfgFile
        {
            internal AcceptedCfgFile(ulong modId, string configName, string displayPath, Dictionary<int, AcceptedCfgEntry> entries)
            { ModId = modId; ConfigName = configName; DisplayPath = displayPath; Entries = entries; }
            internal ulong ModId { get; }
            internal string ConfigName { get; }
            internal string DisplayPath { get; }
            internal Dictionary<int, AcceptedCfgEntry> Entries { get; }
        }
        private sealed class AcceptedCfgEntry
        {
            internal AcceptedCfgEntry(JToken raw, object typed) { Raw = raw; Typed = typed; }
            internal JToken Raw { get; }
            internal object Typed { get; }
        }
        private sealed class CfgFileChange
        {
            internal static readonly CfgFileChange Empty = new CfgFileChange(
                0, string.Empty, string.Empty, Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>());
            internal CfgFileChange(ulong modId, string configName, string displayPath, int[] added, int[] modified, int[] removed)
            { ModId = modId; ConfigName = configName; DisplayPath = displayPath; Added = added; Modified = modified; Removed = removed; }
            internal ulong ModId { get; }
            internal string ConfigName { get; }
            internal string DisplayPath { get; }
            internal IReadOnlyList<int> Added { get; }
            internal IReadOnlyList<int> Modified { get; }
            internal IReadOnlyList<int> Removed { get; }
            internal bool HasChanges => Added.Count + Modified.Count + Removed.Count > 0;
        }
        private sealed class ReloadPlan
        {
            internal ReloadPlan(Dictionary<string, Dictionary<int, object>> rawMaps, Dictionary<string, Dictionary<int, object>> typedMaps)
            { RawMaps = rawMaps; TypedMaps = typedMaps; }
            internal Dictionary<string, Dictionary<int, object>> RawMaps { get; }
            internal Dictionary<string, Dictionary<int, object>> TypedMaps { get; }
        }
    }
}
