using System;
using System.Collections.Generic;
using System.Reflection;
using Config;
using Cysharp.Threading.Tasks;
using EC2BUnofficialPatch.Core;
using EC2BUnofficialPatch.Services;
using HarmonyLib;
using Sdk;
using View.Main;

namespace EC2BUnofficialPatch.Features.Optimization
{
    internal sealed class MapFloorCompatibilityModule : IPluginModule
    {
        private readonly IReadOnlyList<ModuleLogItem> _items;

        internal MapFloorCompatibilityModule()
        {
            var items = new List<ModuleLogItem>();
            if (PluginConfig.MapFloorCompatibility?.Value == true)
                items.Add(new ModuleLogItem("优化", "多mod地图子地点兼容"));
            if (PluginConfig.MapFloorHierarchyExtension?.Value == true)
                items.Add(new ModuleLogItem("优化", "地图子地点扩展"));
            _items = items;
        }

        public string Key => "optimization.map-floor-compatibility";
        public IReadOnlyList<ModuleLogItem> LogItems => _items;

        public void Load(Harmony harmony, PluginServices services)
        {
            MethodInfo merge = AccessTools.Method(typeof(ModCtrl), "MergeCfgsAsync")
                ?? throw new MissingMethodException(typeof(ModCtrl).FullName, "MergeCfgsAsync");
            harmony.Patch(
                merge,
                prefix: new HarmonyMethod(
                    typeof(GameModCfgHotReload),
                    nameof(GameModCfgHotReload.CaptureBaseBeforeModMerge)),
                postfix: new HarmonyMethod(
                    typeof(MapFloorCompatibilityModule),
                    nameof(MergePostfix)));

            MethodInfo refreshFloor = AccessTools.Method(typeof(MapSceneView), "RefreshFloor")
                ?? throw new MissingMethodException(typeof(MapSceneView).FullName, "RefreshFloor");
            MapFloorViewPatches.Initialize(typeof(MapSceneView));
            harmony.Patch(
                refreshFloor,
                prefix: new HarmonyMethod(
                    typeof(MapFloorViewPatches),
                    nameof(MapFloorViewPatches.RefreshFloorPrefix)));

            PatchLog.Registration(
                "优化模块-地图子地点兼容已启用：" +
                $"多mod合并={PluginConfig.MapFloorCompatibility?.Value == true}, " +
                $"分层扩展={PluginConfig.MapFloorHierarchyExtension?.Value == true}");
        }

        public static void MergePostfix(ref UniTask __result)
        {
            __result = RebuildAfterMerge(__result);
        }

        private static async UniTask RebuildAfterMerge(UniTask mergeTask)
        {
            await mergeTask;
            try
            {
                GameModCfgHotReload.RebuildMapFloors();
            }
            catch (Exception exception)
            {
                PatchLog.Error(
                    "优化模块-地图子地点兼容注册失败，已保留重建前地图数据：" +
                    ModuleHost.GetReason(exception));
            }
        }
    }

    internal static class MapFloorViewPatches
    {
        private static FieldInfo _mapIdField;
        private static FieldInfo _cfgField;

        internal static void Initialize(Type viewType)
        {
            _mapIdField = AccessTools.Field(viewType, "mapId");
            _cfgField = AccessTools.Field(viewType, "cfg");
            if (_mapIdField == null || _cfgField == null)
                throw new MissingMemberException("MapSceneView 楼层栏结构不兼容");
        }

        public static bool RefreshFloorPrefix(MapSceneView __instance)
        {
            try
            {
                int mapId = (int)_mapIdField.GetValue(__instance);
                MapCfg current = _cfgField.GetValue(__instance) as MapCfg;
                List<int> floors = MapFloorCompatibilityService.ResolveFloorIds(mapId, current);
                UIItemGroup group = __instance.itemgroup_floor;
                if (group == null)
                    return true;

                bool visible = floors != null && floors.Count > 0;
                group.gameObject.SetActive(visible);
                if (visible)
                    group.SetDatas<int>(floors, null);
                return false;
            }
            catch (Exception exception)
            {
                PatchLog.Error(
                    "优化模块-地图子地点兼容刷新楼层栏失败，回退原版逻辑：" +
                    ModuleHost.GetReason(exception));
                return true;
            }
        }
    }
}
