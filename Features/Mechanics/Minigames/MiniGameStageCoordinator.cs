using System;
using System.Collections.Generic;
using Config;
using EC2BUnofficialPatch.Core;
using Sdk;

namespace EC2BUnofficialPatch.Features.Mechanics.Minigames
{
    internal sealed class MiniGameStageSession
    {
        internal long Token;
        internal MiniGameData Owner;
        internal int NpcId;
        internal int LogicalGameId;
        internal int CfgId;
        internal float Cost;
        internal ResolvedMinigameImplementation FallbackImplementation;
        internal ResolvedMinigameImplementation ActiveImplementation;
        internal HashSet<EmbeddedMinigameLaunchPoint> AllowedEmbeddedLaunches =
            new HashSet<EmbeddedMinigameLaunchPoint>();
        internal int RequestedGameId;
        internal MiniGameFromType LaunchFrom;
        internal bool LaunchObserved;
        internal bool EmbeddedLaunch;
        internal bool StartTalkCallbackReached;
        internal bool SettlementInProgress;
        internal bool Settled;
    }

    internal static class MiniGameStageCoordinator
    {
        private static long _nextToken;

        internal static MiniGameStageSession Current { get; private set; }

        internal static bool TryStartSocialGame(
            MiniGameData owner,
            int npcId,
            int bgId,
            CustomMinigameRegistry registry,
            out bool result)
        {
            result = false;

            if (owner == null)
            {
                PatchLog.Error("机制模块-SocialGame 收到空 MiniGameData。");
                return true;
            }

            if (!Cfg.PersonGrowCfgMap.TryGetValue(npcId, out PersonGrowCfg grow))
            {
                PatchLog.Error($"机制模块-角色缺少 PersonGrowCfg：npc={npcId}");
                return true;
            }

            MiniGameSubData state;
            try
            {
                state = MiniGameStateStore.GetOrCreateForNpc(owner, npcId, grow.minigame);
            }
            catch (Exception exception)
            {
                PatchLog.Exception($"机制模块-读取 NPC 小游戏存档失败：npc={npcId}", exception);
                return true;
            }

            if (!MiniGameStageValidator.TryValidateCurrentStage(
                    npcId,
                    state,
                    registry,
                    out MiniGameStagePlan plan,
                    out string validationError))
            {
                PatchLog.Warning($"机制模块-无法开始社交小游戏：{validationError}");
                return true;
            }

            float cost = state.GetCost();
            if (!Singleton<RoleMgr>.Ins.HasEnoughCost(3, cost, false))
            {
                result = false;
                return true;
            }

            // 上一次流程若因退出或不完整适配没有结算，不能无声丢弃消耗。
            AbortCurrentUnsettled("start-next-social-stage", true);

            string name = DescCtrl.GetFromTag(Cfg.MinigameCfgMap[state.id].name);
            Singleton<RoleMgr>.Ins.GetRole().UpdateAttr(3, -cost, 1f, name, 2);

            int startTalk;
            try
            {
                startTalk = state.Start();
            }
            catch (Exception exception)
            {
                RefundFailedStart(state, cost, name);
                PatchLog.Exception(
                    "机制模块-执行 MiniGameSubData.Start 失败：" +
                    $"npc={npcId}, minigame={state.id}, cfg={state.cfgId}",
                    exception);
                return true;
            }

            MiniGameStageSession session = new MiniGameStageSession
            {
                Token = ++_nextToken,
                Owner = owner,
                NpcId = npcId,
                LogicalGameId = state.id,
                CfgId = plan.Action.id,
                Cost = cost,
                FallbackImplementation = plan.Fallback,
                AllowedEmbeddedLaunches = plan.EmbeddedLaunches
            };
            Current = session;

            PatchLog.Info(
                "机制模块-开始 NPC 小游戏阶段：" +
                $"npc={npcId}, minigame={state.id}, cfg={plan.Action.id}, " +
                $"startTalk={startTalk}, embedded={FormatEmbedded(plan.EmbeddedLaunches)}, " +
                $"fallback={(plan.Fallback == null ? 0 : plan.Fallback.ImplementationId)}");

            result = true;
            if (startTalk > 0)
            {
                long token = session.Token;
                Singleton<CommonEvtMgr>.Ins.ShowTalk(
                    startTalk,
                    delegate { OnStartTalkFlowFinished(token); },
                    bgId,
                    true,
                    true,
                    null);
            }
            else
            {
                OpenFallback(session.Token);
            }

            return true;
        }

        internal static bool TryBindLaunch(
            int requestedGameId,
            MiniGameFromType fromType,
            int typeId,
            CustomMinigameRegistry registry,
            out int logicalGameId,
            out ResolvedMinigameImplementation implementation,
            out bool boundToStage)
        {
            logicalGameId = requestedGameId;
            implementation = null;
            boundToStage = false;

            MiniGameStageSession session = Current;
            if (session != null && !session.Settled)
            {
                EmbeddedMinigameLaunchPoint launchPoint =
                    new EmbeddedMinigameLaunchPoint(fromType, typeId, requestedGameId);
                if (session.AllowedEmbeddedLaunches.Contains(launchPoint))
                {
                    if (!registry.TryResolve(requestedGameId, out implementation) ||
                        !implementation.CanOpenEmbedded)
                    {
                        PatchLog.Error(
                            "机制模块-阶段对话请求了不能内嵌的小游戏：" +
                            $"npc={session.NpcId}, logical={session.LogicalGameId}, " +
                            $"requested={requestedGameId}, from={fromType}, typeId={typeId}");
                        return false;
                    }

                    BindSessionLaunch(session, requestedGameId, fromType, implementation, true);
                    logicalGameId = session.LogicalGameId;
                    boundToStage = true;
                    return true;
                }

                bool fallbackSource =
                    fromType == MiniGameFromType.Level &&
                    (requestedGameId == session.LogicalGameId || typeId == session.CfgId);
                if (fallbackSource)
                {
                    implementation = session.FallbackImplementation;
                    if (implementation == null || !implementation.CanOpenAsFallback)
                    {
                        PatchLog.Error(
                            "机制模块-当前社交阶段没有可用后备实现：" +
                            $"npc={session.NpcId}, logical={session.LogicalGameId}, cfg={session.CfgId}");
                        return false;
                    }

                    BindSessionLaunch(session, requestedGameId, fromType, implementation, false);
                    logicalGameId = session.LogicalGameId;
                    boundToStage = true;
                    return true;
                }
            }

            return registry.TryResolve(requestedGameId, out implementation);
        }

        internal static bool TryMapReportedGameId(int reportedId, out int logicalId)
        {
            MiniGameStageSession session = Current;
            if (session != null && IsActiveReportId(session, reportedId))
            {
                logicalId = session.LogicalGameId;
                return true;
            }

            logicalId = reportedId;
            return false;
        }

        internal static bool IsActiveReportId(MiniGameStageSession session, int reportedId)
        {
            if (session == null) return false;
            int implementationId = session.ActiveImplementation?.ImplementationId ?? 0;
            return reportedId == session.LogicalGameId ||
                   reportedId == session.RequestedGameId ||
                   reportedId == implementationId ||
                   (implementationId == 32 && reportedId == 26);
        }

        internal static void MarkOriginalSettlementStarted()
        {
            if (Current != null)
            {
                Current.SettlementInProgress = true;
            }
        }

        internal static void MarkOriginalSettlementFinished()
        {
            if (Current == null) return;
            Current.SettlementInProgress = false;
            Current.Settled = true;
        }

        internal static bool CompleteFromAdapter(
            long token,
            bool isWin,
            int selectId,
            string source)
        {
            MiniGameStageSession session = Current;
            if (session == null ||
                session.Token != token ||
                session.Settled ||
                session.SettlementInProgress)
            {
                return false;
            }

            if (session.EmbeddedLaunch)
            {
                return SettleEmbedded(token, isWin, selectId, source);
            }

            try
            {
                session.SettlementInProgress = true;
                session.Owner.EndGame(session.LogicalGameId, isWin, selectId);
                return true;
            }
            catch (Exception exception)
            {
                session.SettlementInProgress = false;
                PatchLog.Exception(
                    "机制模块-适配器结算失败：" +
                    $"npc={session.NpcId}, minigame={session.LogicalGameId}, source={source}",
                    exception);
                return false;
            }
        }

        internal static bool SettleEmbedded(
            long token,
            bool isWin,
            int selectId,
            string source)
        {
            MiniGameStageSession session = Current;
            if (session == null || session.Token != token || session.Settled || session.SettlementInProgress)
            {
                return false;
            }

            try
            {
                session.SettlementInProgress = true;
                string name = DescCtrl.GetFromTag(Cfg.MinigameCfgMap[session.LogicalGameId].name);
                Singleton<RoleMgr>.Ins.GetNeedsData().DoSocial(name);

                MiniGameSubData state = MiniGameStateStore.GetOrCreateForNpc(
                    session.Owner,
                    session.NpcId,
                    session.LogicalGameId);
                int beforeCfg = state.cfgId;

                // 内嵌玩法自身负责 Option/Talk 的成功、失败分支。
                // 插件只回写原版社交阶段状态，不重复播放 MinigameActionCfg.winTalk/loseTalk。
                state.End(isWin);
                state.TalkFinish(isWin, selectId);
                EventMgr.Send(1603);

                session.Settled = true;
                session.SettlementInProgress = false;
                PatchLog.Info(
                    "机制模块-内嵌小游戏已回写 NPC 阶段：" +
                    $"npc={session.NpcId}, minigame={session.LogicalGameId}, " +
                    $"win={isWin}, before={beforeCfg}, after={state.cfgId}, source={source}");
                return true;
            }
            catch (Exception exception)
            {
                session.SettlementInProgress = false;
                PatchLog.Exception(
                    "机制模块-内嵌小游戏阶段结算失败：" +
                    $"npc={session.NpcId}, minigame={session.LogicalGameId}, source={source}",
                    exception);
                return false;
            }
        }

        internal static void OnConcreteViewClosed(long token, bool clearEmbeddedWithoutTalkCallback)
        {
            MiniGameStageSession session = Current;
            if (session == null || session.Token != token) return;

            if (!session.EmbeddedLaunch && session.Settled)
            {
                Clear("level-view-closed");
                return;
            }

            if (session.EmbeddedLaunch && session.Settled &&
                (session.StartTalkCallbackReached || clearEmbeddedWithoutTalkCallback))
            {
                Clear(clearEmbeddedWithoutTalkCallback
                    ? "embedded-special-view-finished"
                    : "embedded-view-and-talk-finished");
            }
        }

        internal static void OnImmediateImplementationFinished()
        {
            MiniGameStageSession session = Current;
            if (session != null &&
                !session.EmbeddedLaunch &&
                session.ActiveImplementation?.Original?.ImmediateSuccess == true &&
                session.Settled)
            {
                Clear("immediate-template-finished");
            }
        }

        internal static void AbortCurrentUnsettled(string reason, bool refundCost)
        {
            MiniGameStageSession session = Current;
            if (session != null && !session.Settled && refundCost)
            {
                try
                {
                    MiniGameSubData state = MiniGameStateStore.GetOrCreateForNpc(
                        session.Owner,
                        session.NpcId,
                        session.LogicalGameId);
                    if (state.cnt > 0) state.cnt--;

                    if (session.Cost > 0f)
                    {
                        string name = DescCtrl.GetFromTag(
                            Cfg.MinigameCfgMap[session.LogicalGameId].name);
                        Singleton<RoleMgr>.Ins.GetRole().UpdateAttr(
                            3,
                            session.Cost,
                            1f,
                            name,
                            2);
                        session.Cost = 0f;
                    }
                }
                catch (Exception exception)
                {
                    PatchLog.Exception(
                        "机制模块-终止无效小游戏阶段时回滚消耗失败",
                        exception);
                }
            }

            Clear(reason);
        }

        internal static void Clear(string reason)
        {
            MiniGameStageSession session = Current;
            if (session != null)
            {
                PatchLog.Debug(
                    "机制模块-清理小游戏阶段会话：" +
                    $"reason={reason}, npc={session.NpcId}, " +
                    $"minigame={session.LogicalGameId}, cfg={session.CfgId}, " +
                    $"launched={session.LaunchObserved}, settled={session.Settled}");
            }
            Current = null;
        }

        private static void BindSessionLaunch(
            MiniGameStageSession session,
            int requestedGameId,
            MiniGameFromType fromType,
            ResolvedMinigameImplementation implementation,
            bool embedded)
        {
            session.LaunchObserved = true;
            session.EmbeddedLaunch = embedded;
            session.LaunchFrom = fromType;
            session.RequestedGameId = requestedGameId;
            session.ActiveImplementation = implementation;

            PatchLog.Info(
                "机制模块-绑定小游戏启动到 NPC 阶段：" +
                $"npc={session.NpcId}, logical={session.LogicalGameId}, " +
                $"requested={requestedGameId}, implementation={implementation.ImplementationId}, " +
                $"category={implementation.Original?.Category.ToString() ?? implementation.Kind.ToString()}, " +
                $"from={fromType}, cfg={session.CfgId}");
        }

        private static void OnStartTalkFlowFinished(long token)
        {
            MiniGameStageSession session = Current;
            if (session == null || session.Token != token) return;

            session.StartTalkCallbackReached = true;
            if (session.LaunchObserved)
            {
                if (session.Settled)
                {
                    Clear("embedded-talk-flow-finished");
                }
                else
                {
                    PatchLog.Warning(
                        "机制模块-阶段对话已经打开过内嵌小游戏，但对话结束时尚未收到结算：" +
                        $"npc={session.NpcId}, minigame={session.LogicalGameId}, cfg={session.CfgId}");
                }
                return;
            }

            OpenFallback(token);
        }

        private static void OpenFallback(long token)
        {
            MiniGameStageSession session = Current;
            if (session == null || session.Token != token) return;

            if (session.FallbackImplementation == null)
            {
                PatchLog.Error(
                    "机制模块-startTalk 结束后没有实际打开内嵌小游戏，且当前阶段没有可用后备实现；" +
                    "本阶段将回滚：" +
                    $"npc={session.NpcId}, minigame={session.LogicalGameId}, cfg={session.CfgId}");
                AbortCurrentUnsettled("missing-fallback-after-talk", true);
                return;
            }

            Singleton<FuncMgr>.Ins.OpenMiniGame(
                session.LogicalGameId,
                MiniGameFromType.Level,
                session.CfgId,
                null,
                null,
                null,
                null,
                0);
        }

        private static void RefundFailedStart(MiniGameSubData state, float cost, string name)
        {
            try
            {
                if (state != null && state.cnt > 0) state.cnt--;
                if (cost > 0f)
                {
                    Singleton<RoleMgr>.Ins.GetRole().UpdateAttr(3, cost, 1f, name, 2);
                }
            }
            catch (Exception exception)
            {
                PatchLog.Exception("机制模块-回滚失败的小游戏启动消耗时发生异常", exception);
            }
        }

        private static string FormatEmbedded(
            IEnumerable<EmbeddedMinigameLaunchPoint> launches)
        {
            if (launches == null) return "无";
            string value = string.Join(",", launches);
            return string.IsNullOrWhiteSpace(value) ? "无" : value;
        }
    }
}
