using System;
using System.Collections.Generic;
using System.Linq;
using Config;
using EC2BUnofficialPatch.Core;
using EC2BUnofficialPatch.Workshop;
using GenUI.Main;
using HarmonyLib;
using View.Main;

namespace EC2BUnofficialPatch.Features.Mechanics.MapExtend
{
    internal sealed class MapExtendModule : IPluginModule
    {
        public string Key => "mechanics.map-extend";
        public IReadOnlyList<ModuleLogItem> LogItems => new[] { new ModuleLogItem("机制", "地图扩展与优化") };

        public void Load(Harmony harmony, PluginServices services)
        {
            // The future standalone distribution uses this same provider contract.
            // Only one assembly owns scene sessions, state and object-effect callbacks.
            if (HasProvider())
            {
                PatchLog.Registration("地图扩展与优化已由另一发行版加载，复用现有模块。");
                return;
            }
            MapLocationsPatches.Initialize(services.ContentRoots);
            WorldMapsRuntime.Install(harmony);
            WorldMapNavigation.Install(harmony);
            SceneObjectsRuntime.Install(harmony, services.ContentRoots.Roots);
            Patch(harmony, nameof(MapView.OnOpen), nameof(MapLocationsPatches.OpenPrefix), nameof(MapLocationsPatches.OpenPostfix));
            Patch(harmony, nameof(MapView.OnClose), null, nameof(MapLocationsPatches.ClosePostfix));
            Patch(harmony, nameof(MapView.OnOpenAnimeComp), null, nameof(MapLocationsPatches.OpenAnimePostfix));
            Patch(harmony, nameof(MapView.Refresh), null, nameof(MapLocationsPatches.RefreshPostfix));
            harmony.Patch(AccessTools.Method(typeof(MapView), "OnRenderItem", new[] { typeof(Cell_MapItemUI) })
                ?? throw new MissingMethodException("MapView.OnRenderItem"),
                postfix: new HarmonyMethod(typeof(MapLocationsPatches), nameof(MapLocationsPatches.RenderPostfix)));
            JsonHotReloadApi.EnsureBuiltIn("up.map-extend", "地图扩展与优化", MapLocationsPatches.Reload, 260);
            PatchLog.Registration($"机制模块-地图扩展与优化初始化完成：登记={MapLocationsPatches.Registry.Entries.Count}，原版优先级=2。");
        }

        internal static bool HasProvider() => Harmony.GetPatchInfo(AccessTools.Method(typeof(MapView), nameof(MapView.OnOpen), Type.EmptyTypes))?
            .Prefixes.Any(p => p.PatchMethod.DeclaringType?.Name == nameof(MapLocationsPatches) &&
                p.PatchMethod.Name == nameof(MapLocationsPatches.OpenPrefix)) == true;

        private static void Patch(Harmony harmony, string method, string prefix, string postfix)
        {
            harmony.Patch(AccessTools.Method(typeof(MapView), method, Type.EmptyTypes)
                ?? throw new MissingMethodException(typeof(MapView).FullName, method),
                prefix: prefix == null ? null : new HarmonyMethod(typeof(MapLocationsPatches), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(MapLocationsPatches), postfix));
        }
    }

    internal static class MapLocationsPatches
    {
        internal static MapLocationRegistry Registry { get; private set; }
        private static ReloadInputSnapshot _inputs;

        private static ReloadInputSnapshot CaptureInputs(IReadOnlyList<ContentRoot> roots) => ReloadInputSnapshot.Capture(
            WorldMapsContent.Sources(roots).SelectMany(root => new[] {
                FeatureContentDiscovery.ChildFile(root.Path, MapLocationRegistry.FileName),
                FeatureContentDiscovery.ChildFile(root.Path, WorldMapsContent.FileName) }), true);

        internal static void Initialize(ContentRootCatalog roots)
        {
            Registry = MapLocationRegistry.Load(roots.Roots);
            _inputs = CaptureInputs(roots.Roots);
        }

        internal static JsonHotReloadResult Reload()
        {
            try
            {
                var roots = ContentRootCatalog.Discover().Roots;
                var inputs = CaptureInputs(roots);
                if (inputs.SameAs(_inputs)) return JsonHotReloadResult.Skip("大地图、地点配置及引用图片无变化");
                MapLocationRegistry replacement = MapLocationRegistry.Load(roots);
                WorldMapsRuntime.ReloadContent(roots);
                Registry = replacement;
                _inputs = inputs;
                return JsonHotReloadResult.Completed(replacement.FileCount, replacement.Entries.Count,
                    "大地图与地点登记已更新；关闭并重新打开地图后生效。原版 CFG 修改需重启游戏。");
            }
            catch (Exception exception) { return JsonHotReloadResult.Failed(ModuleHost.GetReason(exception)); }
        }

        public static void OpenPrefix(MapView __instance)
        {
            if (__instance?.gameObject == null || __instance.group_item == null) return;
            MapLocationsSession session = __instance.gameObject.GetComponent<MapLocationsSession>()
                ?? __instance.gameObject.AddComponent<MapLocationsSession>();
            try
            {
                // Read late: game CFGs and enabled Mods are available when the map opens.
                // An open session owns its assets; F9 never destroys its displayed textures.
                MapLocationRegistry registry = WorldMapsRuntime.RegistryFor(Registry);
                WorldMapSession world = __instance.gameObject.GetComponent<WorldMapSession>()
                    ?? __instance.gameObject.AddComponent<WorldMapSession>();
                world.Prepare(__instance);
                session.Build(__instance, registry, world.LabelTemplate);
                WorldMapsRuntime.RequireStations(session);
            }
            catch (Exception exception)
            {
                session.Release();
                __instance.gameObject.GetComponent<WorldMapSession>()?.Release();
                WorldMapsRuntime.ResetWorld();
                PatchLog.Warning("机制模块-地图扩展与优化失败，继续使用原版地图：" + ModuleHost.GetReason(exception));
            }
        }

        public static void OpenPostfix(MapView __instance)
        {
            RefreshPostfix(__instance);
            WorldMapTransitions.Attach(__instance);
            if (__instance.gameObject.GetComponent<WorldMapReturnDriver>() == null) __instance.gameObject.AddComponent<WorldMapReturnDriver>();
        }

        public static void OpenAnimePostfix(MapView __instance) => WorldMapTransitions.Fade(__instance);

        public static void RefreshPostfix(MapView __instance)
        {
            try { __instance?.gameObject?.GetComponent<MapLocationsSession>()?.RefreshVisibility(); }
            catch (Exception exception)
            {
                // Invalid conditions must not leave the custom hit filters blocking native locations.
                __instance?.gameObject?.GetComponent<MapLocationsSession>()?.HideAll();
                PatchLog.Warning("机制模块-地图扩展与优化刷新失败：" + ModuleHost.GetReason(exception));
            }
        }

        public static void RenderPostfix(Cell_MapItemUI cell)
        {
            MapLocationMarker marker = cell?.gameObject?.GetComponent<MapLocationMarker>();
            if (marker == null) return;
            MapLocationEntry entry = marker.Entry;
            cell.txt_name.text = entry.Label ?? ((MapCfg)cell.data).name;
            cell.txt_name.color = entry.TextColor;
            cell.img_name.color = entry.LabelColor;
            cell.btn_name.gameObject.SetActive(entry.ShowLabel);
        }

        public static void ClosePostfix(MapView __instance)
        {
            WorldMapTransitions.Release(__instance);
            __instance?.gameObject?.GetComponent<MapLocationsSession>()?.Release();
            __instance?.gameObject?.GetComponent<WorldMapSession>()?.Release();
        }
    }
}
