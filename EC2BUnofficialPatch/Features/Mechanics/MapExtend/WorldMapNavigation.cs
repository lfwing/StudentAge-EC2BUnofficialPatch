using System;
using System.Linq;
using System.Reflection;
using EC2BUnofficialPatch.Core;
using HarmonyLib;
using Sdk;
using UnityEngine;
using View.Main;

namespace EC2BUnofficialPatch.Features.Mechanics.MapExtend
{
    internal static class WorldMapNavigation
    {
        private static readonly MethodInfo NativeClick = AccessTools.Method(typeof(MapView), "OnClickMap",
            new[] { typeof(int), typeof(Vector2) });

        internal static void Install(Harmony harmony)
        {
            if (NativeClick == null) throw new MissingMethodException("MapView.OnClickMap");
            // Both native back keys close MapView into the home branch; keep them in this region.
            foreach (string method in new[] { nameof(Game.Back), nameof(Game.Esc) })
                harmony.Patch(AccessTools.Method(typeof(Game), method, Type.EmptyTypes),
                    prefix: new HarmonyMethod(typeof(WorldMapNavigation), nameof(BackPrefix)));
        }

        public static bool BackPrefix()
        {
            if (WorldMapsRuntime.ActiveWorldId == 0) return true;
            if (UIMgr.IsShowingMask()) return false;
            BaseView top = UIMgr.GetTopView(Array.Empty<ViewType>());
            WorldMapDefinition world = WorldMapsRuntime.Worlds.FirstOrDefault(item => item.Id == WorldMapsRuntime.ActiveWorldId);
            if (top is MapView map)
            {
                if (map.enableCloseByRightClick && map.viewState == ViewState.Opened) EnterStation(map, world);
                return false;
            }
            // Station scenes also use native CloseView: exit event, then reopen this world's MapView.
            // The active world stays unchanged until the player explicitly chooses another region.
            return true;
        }

        private static void EnterStation(MapView view, WorldMapDefinition world)
        {
            int station = world?.TravelMaps.FirstOrDefault() ?? 0;
            MapLocationMarker marker = view.gameObject.GetComponent<MapLocationsSession>()?.Markers
                .FirstOrDefault(item => item.Entry.MapId == station && item.gameObject.activeInHierarchy);
            if (marker == null)
            {
                PatchLog.Warning("新大地图返回车站失败：缺少可见车站入口，保留当前地图；worldId=" + WorldMapsRuntime.ActiveWorldId);
                return;
            }
            try
            {
                // Use the original click route: circular transition, entering event and MapSceneView.
                NativeClick.Invoke(view, new object[] { station, ((RectTransform)marker.transform).anchoredPosition });
            }
            catch (Exception exception)
            {
                PatchLog.Warning("新大地图返回车站失败：" + ModuleHost.GetReason(exception));
            }
        }
    }
}
