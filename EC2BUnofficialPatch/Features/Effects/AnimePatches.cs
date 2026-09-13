using System;
using System.Collections.Generic;
using System.Reflection;
using Config;
using Effect;
using HarmonyLib;
using Increase;
using Sdk;
using UnityEngine;
using UnityEngine.Events;
using View.Common;
using View.Skill;

namespace EC2BUnofficialPatch.Features.Effects
{
    internal static class AnimeSearchPatches
    {
        internal static bool InitPrefix(AnimeData __instance)
        {
            AnimeSearchService.Initialize(__instance);
            return false;
        }

        internal static void GetSearchCostPostfix(
            ref ValueTuple<int, float, int> __result)
        {
            __result.Item3 = Mathf.Max(
                0,
                __result.Item3 + AnimeSearchService.GetAdditionalCount());
        }

        internal static bool HasAnimeToSearchPrefix(
            AnimeData __instance,
            int _type,
            ref bool __result)
        {
            __result = AnimeSearchService.HasAvailableAnime(__instance, _type);
            return false;
        }

        internal static bool SearchPrefix(
            AnimeData __instance,
            int _type,
            ref List<int> __result)
        {
            __result = AnimeSearchService.ExecuteSearch(__instance, _type);
            return false;
        }

        internal static void NewRoundPrefix(AnimeData __instance, ref int __state)
        {
            __state = AnimeSearchService.GetSearchStep(__instance, 1);
        }

        internal static void NewRoundPostfix(AnimeData __instance, int __state)
        {
            if (!Singleton<RoundMgr>.Ins.IsHoliday())
            {
                return;
            }

            int period = Singleton<RoundMgr>.Ins.GetYear() * 100 +
                         Singleton<RoundMgr>.Ins.GetSeason();
            TheEntity.Role role = Singleton<RoleMgr>.Ins.GetRole();
            int previousPeriod = Mathf.RoundToInt(
                role.GetUnlockValue(AnimeExtensionIds.SearchResetPeriod, false));
            if (previousPeriod == period)
            {
                AnimeSearchService.SetSearchStep(__instance, 1, __state);
                return;
            }

            AnimeSearchService.SetSearchStep(__instance, 1, 1);
            role.SetToggle(
                AnimeExtensionIds.SearchResetPeriod,
                period,
                null,
                true);
        }
    }

    internal static class AnimeWatchPatches
    {
        internal static void WatchPrefix(
            AnimeData __instance,
            int _id,
            ref int __state)
        {
            __state = Cfg.AnimationCfgMap.ContainsKey(_id)
                ? __instance.GetAnimeWatchCnt(_id).Item1
                : -1;
        }

        internal static void WatchPostfix(
            AnimeData __instance,
            int _id,
            int __state)
        {
            if (__state < 0 ||
                !Cfg.AnimationCfgMap.ContainsKey(_id) ||
                __instance.GetAnimeWatchCnt(_id).Item1 <= __state)
            {
                return;
            }

            TheEntity.Role role = Singleton<RoleMgr>.Ins.GetRole();
            role.IncCtrl.Run(
                RoleIncType.DoSomething,
                AnimeExtensionIds.WatchFixedEffects,
                1f);
            int animeLevel = __instance.GetLv().Item1;
            role.IncCtrl.Run(
                RoleIncType.DoSomething,
                AnimeExtensionIds.WatchLevelEffects,
                animeLevel);

            AnimationCfg cfg = Cfg.AnimationCfgMap[_id];
            float godPersonality = AnimeSearchService.GetGodPersonalityBonus();
            if (cfg.level != 3 ||
                Mathf.Approximately(godPersonality, 0f) ||
                !Cfg.AnimationTypeCfgMap.ContainsKey(cfg.type))
            {
                return;
            }

            List<List<float>> effects = Cfg.AnimationTypeCfgMap[cfg.type].effect;
            CommonEvtMgr.RunEffector(
                effects,
                null,
                0,
                0,
                cfg.name,
                godPersonality,
                false);
        }
    }

    internal static class AnimeViewPatches
    {
        private static readonly MethodInfo ShowSearchResultMethod =
            AccessTools.Method(typeof(AnimeView), "ShowSearchResult");

        internal static void InitUiPostfix(AnimeView __instance)
        {
            __instance.btn_refresh.AddClick(
                new UnityAction(() => OnAgainSearch(__instance)));
            __instance.btn_refresh.AddDescription(BuildAgainSearchDescription);
            RefreshView(__instance);
        }

        internal static void RefreshPostfix(AnimeView __instance)
        {
            RefreshView(__instance);
        }

        internal static void RefreshSearchButtonPostfix(
            AnimeView __instance,
            int _type)
        {
            if (_type == 1 && __instance.btn_cost != null)
            {
                __instance.btn_cost.interactable =
                    AnimeSearchService.IsSearchUnlocked() &&
                    __instance.btn_cost.interactable;
            }

            RefreshAgainButton(__instance);
        }

        private static void RefreshView(AnimeView view)
        {
            if (view == null || view.btn_cost == null || view.btn_cost2 == null)
            {
                return;
            }

            bool watchTab = view.btn_cost2.gameObject.activeSelf;
            view.btn_cost.gameObject.SetActive(
                watchTab && AnimeSearchService.IsSearchUnlocked());

            if (view.btn_refresh != null)
            {
                view.btn_refresh.gameObject.SetActive(
                    watchTab && AnimeSearchService.IsAgainSearchUnlocked());
            }

            RefreshAgainButton(view);
        }

        private static void RefreshAgainButton(AnimeView view)
        {
            if (view?.btn_refresh == null || !view.btn_refresh.gameObject.activeSelf)
            {
                return;
            }

            AnimeData data = Singleton<RoleMgr>.Ins.GetAnimeData(true);
            view.btn_refresh.interactable = AnimeSearchService.CanAgainSearch(data);
        }

        private static void OnAgainSearch(AnimeView view)
        {
            AnimeData data = Singleton<RoleMgr>.Ins.GetAnimeData(true);
            List<int> result = AnimeSearchService.ExecuteAgainSearch(data);
            if (result == null || result.Count == 0)
            {
                ToastHelper.Toast(993);
                RefreshAgainButton(view);
                return;
            }

            HintHelper.ShowLoadingResult(
                DescCtrl.GetTxt<string>(222, new[] { "再次找番" }),
                delegate
                {
                    ShowSearchResultMethod?.Invoke(view, new object[] { result });
                    view.Refresh();
                });
        }

        private static DescData? BuildAgainSearchDescription(int type)
        {
            if (Mathf.Abs(type) != 0)
            {
                return null;
            }

            TheEntity.Role role = Singleton<RoleMgr>.Ins.GetRole();
            int year = Singleton<RoundMgr>.Ins.GetYear();
            int lastYear = Mathf.RoundToInt(
                role.GetUnlockValue(AnimeExtensionIds.AgainSearchLastYear, false));
            string status = lastYear == year
                ? "本自然年已经使用"
                : $"消耗{AnimeSearchService.GetAgainSearchCost():0.##}点精力，本自然年可使用一次";
            return new DescData
            {
                title = "再次找番",
                txt = status,
                code = 1000
            };
        }
    }
}
