using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Config;
using LFBetterAudio.Preview;
using LFBetterAudio.Runtime;
using View.Evt;

namespace LFBetterAudio.Effects
{
    /// <summary>
    /// 仅用于把已经在文字开始前执行过的同一条 1163 从原版 DoTextEnd EFFECT 链中移除。
    /// 使用对象引用标记，不按 TalkId 记录，也不实现长文本防重复执行。
    /// </summary>
    internal static class Early1163ExecutionTracker
    {
        private sealed class Marker
        {
        }

        private static readonly ConditionalWeakTable<List<float>, Marker> ExecutedEffects =
            new ConditionalWeakTable<List<float>, Marker>();

        internal static void Mark(List<float> effect)
        {
            if (effect == null)
            {
                return;
            }

            // ConditionalWeakTable 不会因为未走到 DoTextEnd 而永久持有配置对象。
            ExecutedEffects.Remove(effect);
            ExecutedEffects.Add(effect, new Marker());
        }

        internal static bool Consume(List<float> effect)
        {
            return effect != null && ExecutedEffects.Remove(effect);
        }
    }

    internal struct EarlyTalkPlan
    {
        internal bool IsEmpty;
        internal bool HasValidPlayCommand;
    }

    /// <summary>
    /// 1163 的最小提前层：
    /// 1. RefreshTalk 刚进入时只预加载即将使用的音频；
    /// 2. 原版 PlayAudio/PlayRoleEffect 已完成、DoText 即将开始时，单独执行 1163；
    /// 3. 其他原版 EFFECT 仍留在 DoTextEnd 的原有位置。
    /// </summary>
    internal static class Early1163Execution
    {
        internal static EarlyTalkPlan PrepareRuntimeIncomingTalk(
            NewTalkView view,
            int talkId)
        {
            TalkCfg cfg = null;
            if (Cfg.TalkCfgMap != null)
            {
                Cfg.TalkCfgMap.TryGetValue(talkId, out cfg);
            }
            int lastEffectCfgId = RuntimeTalkAccess.GetLastEffectCfgId(view);
            return PrepareIncomingTalk(
                cfg,
                lastEffectCfgId,
                TalkChannel.Runtime,
                null);
        }

        internal static EarlyTalkPlan PreparePreviewIncomingTalk(
            PreviewTalkView view,
            int talkId)
        {
            TalkCfg cfg = null;
            Dictionary<int, TalkCfg> map = PreviewTalkAccess.GetTalkCfgMap(view);
            if (map != null)
            {
                map.TryGetValue(talkId, out cfg);
            }
            int lastEffectCfgId = PreviewTalkAccess.GetLastEffectCfgId(view);
            return PrepareIncomingTalk(
                cfg,
                lastEffectCfgId,
                TalkChannel.Preview,
                PreviewTalkAccess.GetAudioCfgMap(view));
        }

        internal static void ExecuteRuntimeBeforeText(NewTalkView view)
        {
            TalkCfg cfg = RuntimeTalkAccess.GetCurrentCfg(view);
            ExecuteBeforeText(
                view,
                cfg,
                RuntimeTalkAccess.GetLastEffectCfgId(view),
                TalkChannel.Runtime,
                null,
                null,
                GenderDefine.Unknown,
                true);
        }

        internal static void ExecutePreviewBeforeText(PreviewTalkView view)
        {
            TalkCfg cfg = PreviewTalkAccess.GetCurrentCfg(view);
            ExecuteBeforeText(
                view,
                cfg,
                PreviewTalkAccess.GetLastEffectCfgId(view),
                TalkChannel.Preview,
                PreviewTalkAccess.GetAudioCfgMap(view),
                PreviewTalkAccess.GetPersonCfgMap(view),
                PreviewTalkAccess.GetGender(view),
                false);
        }

        internal static void ExecutePreviewEmptyTalk(PreviewTalkView view, int talkId)
        {
            Dictionary<int, TalkCfg> map = PreviewTalkAccess.GetTalkCfgMap(view);
            if (map == null || !map.TryGetValue(talkId, out TalkCfg cfg) || cfg == null ||
                !string.IsNullOrWhiteSpace(cfg.content) ||
                cfg.id == PreviewTalkAccess.GetLastEffectCfgId(view))
            {
                return;
            }

            ExecuteEffects(
                view,
                cfg,
                TalkChannel.Preview,
                PreviewTalkAccess.GetAudioCfgMap(view),
                PreviewTalkAccess.GetPersonCfgMap(view),
                PreviewTalkAccess.GetGender(view),
                false);
        }

        private static EarlyTalkPlan PrepareIncomingTalk(
            TalkCfg cfg,
            int lastEffectCfgId,
            TalkChannel channel,
            Dictionary<int, AudioCfg> previewAudioCfgMap)
        {
            var plan = new EarlyTalkPlan
            {
                IsEmpty = cfg == null || string.IsNullOrWhiteSpace(cfg.content),
                HasValidPlayCommand = false
            };

            if (cfg == null || cfg.id == lastEffectCfgId || cfg.effect == null)
            {
                return plan;
            }

            BetterAudioController controller = BetterAudioController.EnsureInstance();
            if (controller == null)
            {
                return plan;
            }

            foreach (List<float> rawEffect in cfg.effect)
            {
                bool is1163 = BetterAudioEffectEncoding.TryParse(
                    rawEffect,
                    out BetterAudioEffectRequest request,
                    out string parseError);
                if (!is1163 || parseError != null || request == null ||
                    request.Command != BetterAudioCommandKind.Play)
                {
                    continue;
                }

                plan.HasValidPlayCommand = true;
                controller.PreloadRequest(
                    request,
                    channel,
                    previewAudioCfgMap);
            }

            return plan;
        }

        private static void ExecuteBeforeText(
            Sdk.BaseView owner,
            TalkCfg cfg,
            int lastEffectCfgId,
            TalkChannel channel,
            Dictionary<int, AudioCfg> previewAudioCfgMap,
            Dictionary<int, PersonCfg> previewPersonCfgMap,
            GenderDefine previewGender,
            bool markForRuntimeFactory)
        {
            if (owner == null || cfg == null)
            {
                return;
            }

            BetterAudioController controller = BetterAudioController.EnsureInstance();
            if (controller == null)
            {
                Plugin.LogEffectError("无法创建播放控制器，1163 提前执行失败。");
                return;
            }

            try
            {
                if (cfg.id != lastEffectCfgId)
                {
                    ExecuteEffects(
                        owner,
                        cfg,
                        channel,
                        previewAudioCfgMap,
                        previewPersonCfgMap,
                        previewGender,
                        markForRuntimeFactory);
                }
            }
            finally
            {
                // 若新的播放指令无效或不存在，结束仍属于上一 Talk 的单 Talk 会话。
                // 若新会话已成功建立，其 TalkId 已更新，不会被清理。
                controller.FinalizeTextStart(owner, cfg.id);
            }
        }

        private static void ExecuteEffects(
            Sdk.BaseView owner,
            TalkCfg cfg,
            TalkChannel channel,
            Dictionary<int, AudioCfg> previewAudioCfgMap,
            Dictionary<int, PersonCfg> previewPersonCfgMap,
            GenderDefine previewGender,
            bool markForRuntimeFactory)
        {
            if (cfg?.effect == null)
            {
                return;
            }

            BetterAudioController controller = BetterAudioController.EnsureInstance();
            if (controller == null)
            {
                Plugin.LogEffectError("无法创建播放控制器，1163 指令未执行。");
                return;
            }

            // 批次确定性：先完整解析，再把音乐/音效播放指令作为第一批执行，
            // 停止、暂停和恢复等功能性指令作为第二批执行。
            // 因此同一 Talk 中“播放 + 暂停”无论音频是否命中缓存，都能稳定得到
            // “完成加载后直接暂停”的结果。
            var parsedRequests = new List<BetterAudioEffectRequest>();
            foreach (List<float> rawEffect in cfg.effect)
            {
                bool is1163 = BetterAudioEffectEncoding.TryParse(
                    rawEffect,
                    out BetterAudioEffectRequest request,
                    out string parseError);
                if (!is1163)
                {
                    continue;
                }

                if (markForRuntimeFactory)
                {
                    Early1163ExecutionTracker.Mark(rawEffect);
                }

                if (parseError != null)
                {
                    Plugin.LogEffectError(parseError, rawEffect);
                    continue;
                }

                if (request != null)
                {
                    parsedRequests.Add(request);
                }
            }

            ExecuteRequestBatch(
                parsedRequests,
                true,
                controller,
                owner,
                channel,
                cfg.id,
                previewAudioCfgMap,
                previewPersonCfgMap,
                previewGender);

            ExecuteRequestBatch(
                parsedRequests,
                false,
                controller,
                owner,
                channel,
                cfg.id,
                previewAudioCfgMap,
                previewPersonCfgMap,
                previewGender);
        }

        private static void ExecuteRequestBatch(
            IList<BetterAudioEffectRequest> requests,
            bool playBatch,
            BetterAudioController controller,
            Sdk.BaseView owner,
            TalkChannel channel,
            int talkId,
            Dictionary<int, AudioCfg> previewAudioCfgMap,
            Dictionary<int, PersonCfg> previewPersonCfgMap,
            GenderDefine previewGender)
        {
            if (requests == null || controller == null)
            {
                return;
            }

            foreach (BetterAudioEffectRequest request in requests)
            {
                bool isPlay = request.Command == BetterAudioCommandKind.Play;
                if (isPlay != playBatch)
                {
                    continue;
                }

                controller.ExecuteRequest(
                    request,
                    owner,
                    channel,
                    talkId,
                    previewAudioCfgMap,
                    previewPersonCfgMap,
                    previewGender);
            }
        }
    }
}
