using System;
using System.Collections.Generic;
using System.Reflection;
using EC2BUnofficialPatch.Core;
using Effect;
using HarmonyLib;
using View.Skill;

namespace EC2BUnofficialPatch.Features.Effects
{
    internal sealed class AnimeExtensionModule : IPluginModule
    {
        private static readonly IReadOnlyList<ModuleLogItem> Items = new[]
        {
            new ModuleLogItem("效果", "36动画相关修复与扩展")
        };

        public string Key => "effects.anime";

        public IReadOnlyList<ModuleLogItem> LogItems => Items;

        public void Load(Harmony harmony, PluginServices services)
        {
            Patch(
                harmony,
                AccessTools.Method(
                    typeof(CommonEvtMgr),
                    nameof(CommonEvtMgr.GenEffector),
                    new[] { typeof(List<float>), typeof(Effector), typeof(int), typeof(int) }),
                prefix: AccessTools.Method(typeof(AnimeEffectPatches), nameof(AnimeEffectPatches.GenEffectorPrefix)));

            Patch(
                harmony,
                AccessTools.Method(typeof(AnimeData), nameof(AnimeData.Init)),
                prefix: AccessTools.Method(typeof(AnimeSearchPatches), nameof(AnimeSearchPatches.InitPrefix)));
            Patch(
                harmony,
                AccessTools.Method(typeof(AnimeData), nameof(AnimeData.GetSearchCost)),
                postfix: AccessTools.Method(typeof(AnimeSearchPatches), nameof(AnimeSearchPatches.GetSearchCostPostfix)));
            Patch(
                harmony,
                AccessTools.Method(typeof(AnimeData), nameof(AnimeData.HasAnimeToSearch)),
                prefix: AccessTools.Method(typeof(AnimeSearchPatches), nameof(AnimeSearchPatches.HasAnimeToSearchPrefix)));
            Patch(
                harmony,
                AccessTools.Method(typeof(AnimeData), nameof(AnimeData.Search)),
                prefix: AccessTools.Method(typeof(AnimeSearchPatches), nameof(AnimeSearchPatches.SearchPrefix)));
            Patch(
                harmony,
                AccessTools.Method(typeof(AnimeData), nameof(AnimeData.NewRound)),
                prefix: AccessTools.Method(typeof(AnimeSearchPatches), nameof(AnimeSearchPatches.NewRoundPrefix)),
                postfix: AccessTools.Method(typeof(AnimeSearchPatches), nameof(AnimeSearchPatches.NewRoundPostfix)));
            Patch(
                harmony,
                AccessTools.Method(typeof(AnimeData), nameof(AnimeData.Watch)),
                prefix: AccessTools.Method(typeof(AnimeWatchPatches), nameof(AnimeWatchPatches.WatchPrefix)),
                postfix: AccessTools.Method(typeof(AnimeWatchPatches), nameof(AnimeWatchPatches.WatchPostfix)));

            Patch(
                harmony,
                AccessTools.Method(typeof(AnimeView), nameof(AnimeView.InitUI)),
                postfix: AccessTools.Method(typeof(AnimeViewPatches), nameof(AnimeViewPatches.InitUiPostfix)));
            Patch(
                harmony,
                AccessTools.Method(typeof(AnimeView), nameof(AnimeView.OnOpen)),
                postfix: AccessTools.Method(typeof(AnimeViewPatches), nameof(AnimeViewPatches.RefreshPostfix)));
            Patch(
                harmony,
                AccessTools.Method(typeof(AnimeView), nameof(AnimeView.Refresh)),
                postfix: AccessTools.Method(typeof(AnimeViewPatches), nameof(AnimeViewPatches.RefreshPostfix)));
            Patch(
                harmony,
                AccessTools.Method(typeof(AnimeView), "Select"),
                postfix: AccessTools.Method(typeof(AnimeViewPatches), nameof(AnimeViewPatches.RefreshPostfix)));
            Patch(
                harmony,
                AccessTools.Method(typeof(AnimeView), "RefreshSearchBtn"),
                postfix: AccessTools.Method(typeof(AnimeViewPatches), nameof(AnimeViewPatches.RefreshSearchButtonPostfix)));
        }

        private static void Patch(
            Harmony harmony,
            MethodInfo target,
            MethodInfo prefix = null,
            MethodInfo postfix = null)
        {
            if (target == null)
            {
                throw new MissingMethodException("看番扩展所需的游戏方法不存在。");
            }

            harmony.Patch(
                target,
                prefix == null ? null : new HarmonyMethod(prefix),
                postfix == null ? null : new HarmonyMethod(postfix));
        }
    }
}
