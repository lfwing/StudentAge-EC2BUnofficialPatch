using System;
using System.Collections.Generic;
using System.Reflection;
using Config;
using HarmonyLib;
using Increase;
using Sdk;
using TheEntity;
using UnityEngine;

namespace EC2BUnofficialPatch.Features.Effects
{
    internal static class AnimeExtensionIds
    {
        internal const int AnimeCount = 1601;
        internal const int AnimeAttrFromLevel = 1602;
        internal const int AnimeGodWeight = 1603;
        internal const int AnimeGodPersonality = 1604;
        internal const int ToggleAnimeSearch = 9003;
        internal const int ToggleAnimeConvention = 9005;

        // 1211/1212 follow the original DoSomething sequence, whose last used anime-adjacent key is 1210.
        internal const int WatchFixedEffects = 1211;
        internal const int WatchLevelEffects = 1212;

        // Plugin-private persisted toggle keys. They deliberately do not overlap game configuration IDs.
        internal const int AgainSearchUnlocked = 936001;
        internal const int AgainSearchEnergyCost = 936002;
        internal const int AgainSearchLastYear = 936003;
        internal const int SearchResetPeriod = 936004;
    }

    internal static class AnimeSearchService
    {
        private const int DefaultSearchGrade = 7;

        private static readonly FieldInfo SearchTimesField =
            AccessTools.Field(typeof(AnimeData), "searchTimes");

        internal static bool IsSearchUnlocked()
        {
            Role role = Singleton<RoleMgr>.Ins.GetRole();
            return role != null &&
                   (role.Grade >= DefaultSearchGrade ||
                    role.IsUnlock(AnimeExtensionIds.ToggleAnimeSearch));
        }

        internal static bool IsAgainSearchUnlocked()
        {
            Role role = Singleton<RoleMgr>.Ins.GetRole();
            return role != null && role.IsUnlock(AnimeExtensionIds.AgainSearchUnlocked);
        }

        internal static float GetAgainSearchCost()
        {
            Role role = Singleton<RoleMgr>.Ins.GetRole();
            return role == null
                ? 0f
                : Mathf.Max(0f, role.GetUnlockValue(AnimeExtensionIds.AgainSearchEnergyCost, false));
        }

        internal static bool CanAgainSearch(AnimeData data)
        {
            if (data == null || !IsAgainSearchUnlocked())
            {
                return false;
            }

            Role role = Singleton<RoleMgr>.Ins.GetRole();
            int year = Singleton<RoundMgr>.Ins.GetYear();
            int lastYear = Mathf.RoundToInt(
                role.GetUnlockValue(AnimeExtensionIds.AgainSearchLastYear, false));
            return lastYear != year &&
                   Singleton<RoleMgr>.Ins.HasEnoughCost(7, GetAgainSearchCost(), false) &&
                   BuildPool(data, 1, includeOwned: false).Count > 0;
        }

        internal static void Initialize(AnimeData data)
        {
            if (data == null || data.curAnimes != null)
            {
                return;
            }

            List<WeightedAnime> available = BuildPool(data, 1, includeOwned: false);
            available.RemoveAll(item => item.Config.level == 3);
            data.curAnimes = new List<int>();
            int count = Mathf.Min(2, available.Count);
            for (int index = 0; index < count; index++)
            {
                AnimationCfg selected = TakeWeighted(available);
                if (selected == null)
                {
                    break;
                }

                data.curAnimes.Add(selected.id);
            }
        }

        internal static bool HasAvailableAnime(AnimeData data, int mode)
        {
            if (data == null || mode < 0 || mode > 1)
            {
                return false;
            }

            if (mode == 1 && !IsSearchUnlocked())
            {
                return false;
            }

            return BuildPool(data, mode, includeOwned: false).Count > 0;
        }

        internal static List<int> ExecuteSearch(AnimeData data, int mode)
        {
            if (data == null || mode < 0 || mode > 1)
            {
                return null;
            }

            if (mode == 1 && !IsSearchUnlocked())
            {
                return null;
            }

            ValueTuple<int, float, int> searchCost = data.GetSearchCost(mode);
            if (!Singleton<RoleMgr>.Ins.HasEnoughCost(
                    searchCost.Item1,
                    searchCost.Item2,
                    false))
            {
                return null;
            }

            List<int> result = Draw(data, mode, Mathf.Max(0, searchCost.Item3));
            if (result.Count == 0)
            {
                return result;
            }

            if (!Singleton<RoleMgr>.Ins.Cost(
                    searchCost.Item1,
                    searchCost.Item2,
                    null,
                    true))
            {
                return null;
            }

            IncrementSearchStep(data, mode);
            Commit(data, result);
            return result;
        }

        internal static List<int> ExecuteAgainSearch(AnimeData data)
        {
            if (!CanAgainSearch(data))
            {
                return null;
            }

            int count = data.GetSearchCost(1).Item3;
            List<int> result = Draw(data, 1, Mathf.Max(0, count));
            if (result.Count == 0)
            {
                return result;
            }

            float cost = GetAgainSearchCost();
            if (!Singleton<RoleMgr>.Ins.Cost(7, cost, null, true))
            {
                return null;
            }

            Commit(data, result);
            Role role = Singleton<RoleMgr>.Ins.GetRole();
            role.SetToggle(
                AnimeExtensionIds.AgainSearchLastYear,
                Singleton<RoundMgr>.Ins.GetYear(),
                null,
                true);
            return result;
        }

        internal static int GetAdditionalCount()
        {
            Role role = Singleton<RoleMgr>.Ins.GetRole();
            return role == null
                ? 0
                : Mathf.RoundToInt(
                    role.IncCtrl.GetValue(RoleIncType.OtherAttrInc, AnimeExtensionIds.AnimeCount));
        }

        internal static float GetGodWeightBonus()
        {
            Role role = Singleton<RoleMgr>.Ins.GetRole();
            return role == null
                ? 0f
                : role.IncCtrl.GetValue(
                    RoleIncType.OtherAttrInc,
                    AnimeExtensionIds.AnimeGodWeight);
        }

        internal static float GetGodPersonalityBonus()
        {
            Role role = Singleton<RoleMgr>.Ins.GetRole();
            return role == null
                ? 0f
                : role.IncCtrl.GetValue(
                    RoleIncType.OtherAttrInc,
                    AnimeExtensionIds.AnimeGodPersonality);
        }

        internal static int GetSearchStep(AnimeData data, int mode)
        {
            int[] values = GetSearchTimes(data, create: false);
            return values == null || mode < 0 || mode >= values.Length
                ? 1
                : Mathf.Max(1, values[mode]);
        }

        internal static void SetSearchStep(AnimeData data, int mode, int value)
        {
            int[] values = GetSearchTimes(data, create: true);
            if (mode >= 0 && mode < values.Length)
            {
                values[mode] = Mathf.Max(1, value);
            }
        }

        private static List<int> Draw(AnimeData data, int mode, int count)
        {
            List<WeightedAnime> pool = BuildPool(data, mode, includeOwned: false);
            List<WeightedAnime> gods = new List<WeightedAnime>();
            if (mode == 0)
            {
                for (int index = pool.Count - 1; index >= 0; index--)
                {
                    if (pool[index].Config.level == 3)
                    {
                        gods.Add(pool[index]);
                        pool.RemoveAt(index);
                    }
                }
            }

            List<int> result = new List<int>();
            for (int index = 0; index < count; index++)
            {
                AnimationCfg selected = null;
                if (mode == 0 && index == 1 && gods.Count > 0)
                {
                    selected = TakeWeighted(gods);
                }
                else if (pool.Count > 0)
                {
                    selected = TakeWeighted(pool);
                }
                else if (gods.Count > 0)
                {
                    selected = TakeWeighted(gods);
                }

                if (selected == null)
                {
                    break;
                }

                result.Add(selected.id);
            }

            return result;
        }

        private static List<WeightedAnime> BuildPool(
            AnimeData data,
            int mode,
            bool includeOwned)
        {
            List<WeightedAnime> result = new List<WeightedAnime>();
            int year = Singleton<RoundMgr>.Ins.GetYear();
            int favorType = data.GetFavorType();
            float godBonus = GetGodWeightBonus();

            foreach (KeyValuePair<int, AnimationCfg> pair in Cfg.AnimationCfgMap)
            {
                AnimationCfg cfg = pair.Value;
                if (cfg == null ||
                    cfg.time > year ||
                    (!includeOwned &&
                     data.curAnimes != null &&
                     data.curAnimes.Contains(pair.Key)))
                {
                    continue;
                }

                float weight = cfg.weight;
                if (cfg.level == 3)
                {
                    weight += godBonus;
                }

                if (cfg.type == favorType)
                {
                    weight *= 1.5f;
                }

                weight = Mathf.Max(0f, weight);
                if (weight > 0f)
                {
                    result.Add(new WeightedAnime(cfg, weight));
                }
            }

            return result;
        }

        private static AnimationCfg TakeWeighted(List<WeightedAnime> pool)
        {
            if (pool == null || pool.Count == 0)
            {
                return null;
            }

            float total = 0f;
            foreach (WeightedAnime item in pool)
            {
                total += item.Weight;
            }

            if (total <= 0f)
            {
                return null;
            }

            float roll = UnityEngine.Random.Range(0f, total);
            for (int index = 0; index < pool.Count; index++)
            {
                WeightedAnime item = pool[index];
                if (roll <= item.Weight)
                {
                    pool.RemoveAt(index);
                    return item.Config;
                }

                roll -= item.Weight;
            }

            AnimationCfg fallback = pool[pool.Count - 1].Config;
            pool.RemoveAt(pool.Count - 1);
            return fallback;
        }

        private static void Commit(AnimeData data, List<int> result)
        {
            if (data.curAnimes == null)
            {
                data.curAnimes = new List<int>();
            }

            foreach (int id in result)
            {
                if (!data.curAnimes.Contains(id))
                {
                    data.curAnimes.Add(id);
                }
            }
        }

        private static void IncrementSearchStep(AnimeData data, int mode)
        {
            SetSearchStep(data, mode, GetSearchStep(data, mode) + 1);
        }

        private static int[] GetSearchTimes(AnimeData data, bool create)
        {
            int[] values = SearchTimesField?.GetValue(data) as int[];
            if (values == null && create)
            {
                values = new[] { 1, 1 };
                SearchTimesField?.SetValue(data, values);
            }

            return values;
        }

        private sealed class WeightedAnime
        {
            internal WeightedAnime(AnimationCfg config, float weight)
            {
                Config = config;
                Weight = weight;
            }

            internal AnimationCfg Config { get; }

            internal float Weight { get; }
        }
    }
}
