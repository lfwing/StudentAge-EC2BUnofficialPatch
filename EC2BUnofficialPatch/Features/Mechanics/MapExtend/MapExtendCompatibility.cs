using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using View.Main;
using EC2BUnofficialPatch.Core;
using EC2BUnofficialPatch.Services;

namespace EC2BUnofficialPatch.Features.Mechanics.MapExtend
{
    // The two distributions have separate CLR types. Only the installed provider
    // owns live state; forwarding uses game types and framework collections.
    internal static class MapExtendCompatibility
    {
        private const BindingFlags All = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        internal static Type CfgProvider => Harmony.GetPatchInfo(AccessTools.Method(typeof(ModCtrl), "MergeCfgsAsync"))?
            .Prefixes.FirstOrDefault(p => p.PatchMethod.DeclaringType?.Name == nameof(GameModCfgHotReload) &&
                p.PatchMethod.Name == nameof(GameModCfgHotReload.CaptureBaseBeforeModMerge))?.PatchMethod.DeclaringType;
        internal static void EnsureCfgCapture(Harmony harmony)
        {
            if (CfgProvider == null) harmony.Patch(AccessTools.Method(typeof(ModCtrl), "MergeCfgsAsync"),
                prefix: new HarmonyMethod(typeof(GameModCfgHotReload), nameof(GameModCfgHotReload.CaptureBaseBeforeModMerge)));
        }
        internal static bool RecordSharedSource(ulong mod, string path)
        {
            Type provider = CfgProvider;
            if (provider == null || provider.Assembly == typeof(MapExtendCompatibility).Assembly) return false;
            provider.GetMethod(nameof(GameModCfgHotReload.RecordSource), All).Invoke(null, new object[] { mod, path });
            return true;
        }
        internal static JsonHotReloadResult ReloadCapturedCfg()
        {
            Type provider = CfgProvider;
            if (provider == null || provider.Assembly == typeof(MapExtendCompatibility).Assembly) return GameModCfgHotReload.Reload();
            object result = provider.GetMethod(nameof(GameModCfgHotReload.Reload), All).Invoke(null, null);
            Type type = result.GetType();
            string message = (string)type.GetProperty("Message").GetValue(result);
            if (!(bool)type.GetProperty("Success").GetValue(result)) return JsonHotReloadResult.Failed(message);
            if ((bool)type.GetProperty("Skipped").GetValue(result)) return JsonHotReloadResult.Skip(message);
            return JsonHotReloadResult.Completed((int)type.GetProperty("FileCount").GetValue(result),
                (int)type.GetProperty("EntryCount").GetValue(result), message);
        }
        internal static void RebuildCapturedFloors()
        {
            Type provider = CfgProvider ?? typeof(GameModCfgHotReload);
            provider.GetMethod(nameof(GameModCfgHotReload.RebuildMapFloors), All).Invoke(null, null);
        }
        internal static Type FloorProvider => Harmony.GetPatchInfo(AccessTools.Method(typeof(MapSceneView), "RefreshFloor"))?
            .Prefixes.FirstOrDefault(p => p.PatchMethod.DeclaringType?.Name == nameof(MapFloorViewPatches) &&
                p.PatchMethod.Name == nameof(MapFloorViewPatches.RefreshFloorPrefix))?.PatchMethod.DeclaringType;
        internal static bool OtherFloorProvider => FloorProvider != null && FloorProvider.Assembly != typeof(MapExtendCompatibility).Assembly;

        internal static void EnsureActiveFunction()
        {
            Type provider = Harmony.GetPatchInfo(AccessTools.Method(typeof(MapView), "OnOpen", Type.EmptyTypes))?
                .Prefixes.FirstOrDefault(p => p.PatchMethod.DeclaringType?.Name == nameof(MapLocationsPatches) &&
                    p.PatchMethod.Name == nameof(MapLocationsPatches.OpenPrefix))?.PatchMethod.DeclaringType;
            if (provider != null)
                provider.Assembly.GetType(provider.Namespace + ".WorldMapsRuntime", true)
                    .GetMethod(nameof(WorldMapsRuntime.EnsureFunction), All).Invoke(null, null);
            else if (WorldMapsRuntime.IsInstalled) WorldMapsRuntime.EnsureFunction();
        }

        internal static bool TryRootMap(int mapId, out int root)
        {
            root = mapId;
            Type provider = FloorProvider;
            if (provider == null || provider.Assembly == typeof(MapExtendCompatibility).Assembly) return false;
            root = (int)provider.Assembly.GetType(provider.Namespace + ".MapFloorCompatibilityService", true)
                .GetMethod(nameof(MapFloorCompatibilityService.RootMapId), All).Invoke(null, new object[] { mapId });
            return true;
        }

        internal static MapFloorRebuildResult RebuildFloors(Dictionary<int, List<int>> baseline,
            IReadOnlyList<MapFloorCfgDocument> documents, bool merge, bool nested)
        {
            Type provider = FloorProvider;
            if (provider == null || provider.Assembly == typeof(MapExtendCompatibility).Assembly)
                return MapFloorCompatibilityService.Rebuild(baseline, documents, merge || MapExtendModule.HasProvider(), nested);
            Assembly assembly = provider.Assembly;
            Type entryType = assembly.GetType(provider.Namespace + ".MapFloorCfgEntry", true);
            Type documentType = assembly.GetType(provider.Namespace + ".MapFloorCfgDocument", true);
            Array converted = Array.CreateInstance(documentType, documents.Count);
            for (int i = 0; i < documents.Count; i++)
            {
                Array entries = Array.CreateInstance(entryType, documents[i].Entries.Count);
                for (int j = 0; j < entries.Length; j++)
                {
                    MapFloorCfgEntry entry = documents[i].Entries[j];
                    entries.SetValue(Activator.CreateInstance(entryType, All, null, new object[] { entry.Id, entry.Floors }, null), j);
                }
                converted.SetValue(Activator.CreateInstance(documentType, All, null, new object[] { documents[i].ModId, entries }, null), i);
            }
            Type config = assembly.GetTypes().Single(t => t.Name == "PluginConfig");
            object setting = config.GetProperty("MapFloorHierarchyExtension", All)?.GetValue(null);
            if (setting != null) nested = (bool)setting.GetType().GetProperty("Value").GetValue(setting);
            object result = assembly.GetType(provider.Namespace + ".MapFloorCompatibilityService", true)
                .GetMethod(nameof(MapFloorCompatibilityService.Rebuild), All).Invoke(null,
                    new object[] { baseline, converted, merge || MapExtendModule.HasProvider(), nested });
            Type resultType = result.GetType();
            return new MapFloorRebuildResult((int)resultType.GetProperty("ChangedParents", All).GetValue(result),
                (int)resultType.GetProperty("DeclaredAdded", All).GetValue(result),
                (int)resultType.GetProperty("AutoAdded", All).GetValue(result));
        }
    }
}
