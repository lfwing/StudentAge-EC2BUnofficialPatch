using System;
using System.Reflection;
using Config;
using EC2BUnofficialPatch.Core;
using HarmonyLib;
using Sdk;
using UnityEngine;
using View.Main;

namespace EC2BUnofficialPatch.Features.Effects
{
    internal static class MapMoveEffectPatches
    {
        private const int MoveToMapSubType = 1;

        internal static bool OnRunPrefix(int ___subType, int ___id)
        {
            if (___subType != MoveToMapSubType)
            {
                return true;
            }

            if (MapMoveService.MoveTo(___id))
                PatchLog.Info($"效果模块-100,1地点移动已执行：effect=100,1,{___id}");
            return false;
        }

        internal static bool OnToStringPrefix(
            int ___subType,
            int ___id,
            int _type,
            ref string __result)
        {
            if (___subType != MoveToMapSubType)
            {
                return true;
            }

            __result = MapMoveService.GetDescription(___id, _type);
            return false;
        }
    }

    internal static class MapMoveService
    {
        private static readonly MethodInfo ChangeSceneMethod =
            AccessTools.Method(typeof(MapSceneView), "ChangeScene", new[] { typeof(int) });

        internal static string GetDescription(int mapId, int textType)
        {
            MapCfg map;
            if (Cfg.MapCfgMap != null &&
                Cfg.MapCfgMap.TryGetValue(mapId, out map) &&
                map != null &&
                !string.IsNullOrEmpty(map.name))
            {
                return "移动到" + HtmlTxtUtil.ToCommonName(map.name, textType) + "地点";
            }

            return "移动到地图" + mapId + "地点";
        }

        internal static bool IsMoveEffect(int subType)
        {
            return subType == 1;
        }

        internal static bool MoveTo(int mapId)
        {
            MapCfg map;
            if (Cfg.MapCfgMap == null ||
                !Cfg.MapCfgMap.TryGetValue(mapId, out map) ||
                map == null)
            {
                Debug.LogWarning(
                    "[EC2BUnofficialPatch] 无法执行[100,1," + mapId +
                    "]：MapCfg 中不存在该地点。");
                return false;
            }

            Singleton<FuncMgr>.Ins.GetMapData().UnlockMapScene(mapId);

            if (mapId == 1)
            {
                MoveHome();
                return true;
            }

            MapSceneView openedScene = UIMgr.GetOpeningView<MapSceneView>();
            if (openedScene != null)
            {
                if (ChangeSceneMethod == null)
                {
                    throw new MissingMethodException(
                        typeof(MapSceneView).FullName,
                        "ChangeScene");
                }

                ChangeSceneMethod.Invoke(openedScene, new object[] { mapId });
                return true;
            }

            CloseImmediately(UIMgr.GetOpeningView<MainView>());
            CloseImmediately(UIMgr.GetOpeningView<MapView>());
            UIMgr.OpenView<MapSceneView>(
                UILayerType.None,
                null,
                new object[] { mapId });
            return true;
        }

        private static void MoveHome()
        {
            Singleton<FuncMgr>.Ins.RecordMap(1, 0, -1);
            CloseImmediately(UIMgr.GetOpeningView<MapSceneView>());
            CloseImmediately(UIMgr.GetOpeningView<MapView>());
            UIMgr.OpenView<MainView>(
                UILayerType.None,
                null,
                Array.Empty<object>());
        }

        private static void CloseImmediately(BaseView view)
        {
            if (view != null)
            {
                UIMgr.CloseView(view, false, true);
            }
        }
    }
}
