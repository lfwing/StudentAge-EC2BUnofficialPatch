using System;
using System.Reflection;
using Config;
using EC2BUnofficialPatch.Core;
using HarmonyLib;
using Sdk;
using UnityEngine;
using View.Main;

namespace EC2BUnofficialPatch.Features.Mechanics.MapExtend
{
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
                // Both author-tool clicks and 100,1,1 effects use this shared route.
                // An existing cycle already covers the move; avoid nesting masks.
                if (UIMgr.IsShowingMask()) MoveHome();
                else UIMgr.ShowCycleMask(Vector2.zero, MoveHome, .4f, true, false);
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
