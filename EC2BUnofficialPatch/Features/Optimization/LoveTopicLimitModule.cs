using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using EC2BUnofficialPatch.Core;
using HarmonyLib;
using Sdk;

namespace EC2BUnofficialPatch.Features.Optimization
{
    internal sealed class LoveTopicLimitModule : IPluginModule
    {
        private static readonly IReadOnlyList<ModuleLogItem> Items = new[]
        {
            new ModuleLogItem("优化", "情侣话题每回合次数")
        };

        public string Key => "optimization.love-topics";
        public IReadOnlyList<ModuleLogItem> LogItems => Items;

        public void Load(Harmony harmony, PluginServices services)
        {
            // 启动自检阶段先验证配置值；非法值只在这里输出一次警告。
            LoveTopicPatches.GetSocialTopicLimit();
            Patch(harmony, AccessTools.Method(typeof(LoveData), "CanSocialTopic"), prefix: nameof(LoveTopicPatches.CanSocialTopicPrefix));
            Patch(
                harmony,
                AccessTools.Method(typeof(LoveData), "SocialTopic"),
                prefix: nameof(LoveTopicPatches.SocialTopicPrefix),
                postfix: nameof(LoveTopicPatches.SocialTopicPostfix),
                finalizer: nameof(LoveTopicPatches.SocialTopicFinalizer));
        }

        private static void Patch(Harmony harmony, MethodBase target, string prefix = null, string postfix = null, string finalizer = null)
        {
            if (target == null)
                throw new MissingMethodException("情侣话题补丁目标不存在");
            harmony.Patch(
                target,
                prefix: prefix == null ? null : new HarmonyMethod(typeof(LoveTopicPatches), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(LoveTopicPatches), postfix),
                finalizer: finalizer == null ? null : new HarmonyMethod(typeof(LoveTopicPatches), finalizer));
        }
    }

    internal static class LoveTopicPatches
    {
        private static string _lastWarnedValue;

        internal static bool CanSocialTopicPrefix(int ___socialTopicCntThisRound, ref bool __result)
        {
            __result = ___socialTopicCntThisRound < GetSocialTopicLimit();
            return false;
        }

        internal static bool SocialTopicPrefix(
            ref int ___socialTopicCntThisRound,
            ref int __state,
            ref bool __result)
        {
            __state = -1;
            int current = ___socialTopicCntThisRound;
            if (current >= GetSocialTopicLimit())
            {
                __result = false;
                return false;
            }

            // 原版方法内部硬编码 “> 0 即拒绝”。仅在调用期间归零，借用原版扣除热情与 ShowEvent 流程。
            if (current > 0)
            {
                __state = current;
                ___socialTopicCntThisRound = 0;
            }
            return true;
        }

        internal static void SocialTopicPostfix(
            LoveData __instance,
            int _evtId,
            bool __result,
            ref int ___socialTopicCntThisRound,
            ref List<int> ___topicsThisRound,
            ref int __state)
        {
            if (__state >= 0)
                ___socialTopicCntThisRound += __state;
            __state = -1;

            if (__result)
                RefreshTopics(__instance, _evtId, ref ___topicsThisRound);
        }

        internal static Exception SocialTopicFinalizer(
            Exception __exception,
            ref int ___socialTopicCntThisRound,
            ref int __state)
        {
            if (__state >= 0)
            {
                ___socialTopicCntThisRound += __state;
                __state = -1;
            }
            return __exception;
        }

        internal static int GetSocialTopicLimit()
        {
            string raw = PluginConfig.LoveTopicLimit?.Value;
            int value;
            if (!string.IsNullOrWhiteSpace(raw) &&
                int.TryParse(raw.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value) &&
                value > 0)
                return value;

            if (!string.Equals(_lastWarnedValue, raw, StringComparison.Ordinal))
            {
                _lastWarnedValue = raw;
                PatchLog.Warning($"优化模块-情侣话题每回合次数非法：value={raw ?? "<null>"}；仅接受正整数，已使用安全值 1");
            }
            return 1;
        }

        private static void RefreshTopics(LoveData data, int usedEventId, ref List<int> topics)
        {
            if (topics == null)
                topics = new List<int>();

            topics.RemoveAll(id => id == usedEventId);
            List<int> unique = new List<int>();
            foreach (int id in topics)
            {
                if (id != usedEventId && !unique.Contains(id) && unique.Count < 3)
                    unique.Add(id);
            }
            topics = unique;

            if (topics.Count < 3)
            {
                // NPC、condition、maxcount 与历史仍全部由原版判断；这里只从完整合法集合补足三个 UI 槽位。
                List<int> candidates = Singleton<CommonEvtMgr>.Ins.GetEnableEvtIds(22, data.loverId, 0, int.MaxValue);
                if (candidates != null)
                {
                    foreach (int id in candidates)
                    {
                        if (id != usedEventId && !topics.Contains(id))
                        {
                            topics.Add(id);
                            if (topics.Count == 3)
                                break;
                        }
                    }
                }
            }
        }
    }
}
