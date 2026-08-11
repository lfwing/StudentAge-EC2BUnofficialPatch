using System;
using System.Collections.Generic;
using System.Reflection;
using Config;
using EC2BUnofficialPatch.Core;
using HarmonyLib;
using Sdk;

namespace EC2BUnofficialPatch.Features.Mechanics.Minigames
{
    internal static class MiniGameMechanicsPatches
    {
        [ThreadStatic]
        private static ConcreteClosePatchState _closeContext;

        private static CustomMinigameRegistry _registry;

        internal static void Initialize(CustomMinigameRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            MiniGameStageValidator.ResetDefinitionValidation();
        }

        internal static bool SocialGamePrefix(
            MiniGameData __instance,
            int _npcId,
            int _bgId,
            ref bool __result)
        {
            try
            {
                // BepInEx Awake 可能早于游戏 cfg 初始化。阶段定义检查是诊断功能，
                // 延迟到首次社交小游戏时重试，但成功后只执行一次。
                MiniGameStageValidator.TryLogDefinitionProblems(_registry);

                bool handled = MiniGameStageCoordinator.TryStartSocialGame(
                    __instance,
                    _npcId,
                    _bgId,
                    _registry,
                    out __result);
                return !handled;
            }
            catch (Exception exception)
            {
                PatchLog.Exception($"机制模块-SocialGame 补丁异常：npc={_npcId}", exception);
                __result = false;
                MiniGameStageCoordinator.AbortCurrentUnsettled(
                    "social-prefix-exception",
                    true);
                return false;
            }
        }

        internal static bool GetGameByNpcPrefix(
            MiniGameData __instance,
            int _npcId,
            ref MiniGameSubData __result)
        {
            if (!Cfg.PersonGrowCfgMap.ContainsKey(_npcId))
            {
                return true;
            }

            __result = MiniGameStateStore.GetOrCreateForNpc(__instance, _npcId);
            return false;
        }

        internal static bool GetGamePrefix(
            MiniGameData __instance,
            int _id,
            ref MiniGameSubData __result)
        {
            MiniGameStageSession session = MiniGameStageCoordinator.Current;
            if (session == null || !MiniGameStageCoordinator.IsActiveReportId(session, _id))
            {
                return true;
            }

            __result = MiniGameStateStore.GetOrCreateForNpc(
                __instance,
                session.NpcId,
                session.LogicalGameId);
            return false;
        }

        internal static bool OpenMiniGamePrefix(
            ref int _gameId,
            MiniGameFromType _type,
            int _typeId,
            List<double> _parms,
            ref Action _success,
            ref Action _fail,
            ref Action<float> _result,
            ref OpenMiniGamePatchState __state)
        {
            int requestedId = _gameId;
            if (_registry == null ||
                !MiniGameStageCoordinator.TryBindLaunch(
                    requestedId,
                    _type,
                    _typeId,
                    _registry,
                    out int logicalId,
                    out ResolvedMinigameImplementation implementation,
                    out bool boundToStage))
            {
                PatchLog.Error(
                    "机制模块-无法打开小游戏：没有原版分支或有效注册，" +
                    $"minigame={requestedId}, from={_type}, typeId={_typeId}");
                _fail?.Invoke();
                if (_fail == null) _result?.Invoke(0f);

                if (MiniGameStageCoordinator.Current != null)
                {
                    MiniGameStageCoordinator.AbortCurrentUnsettled(
                        "unsupported-minigame-launch",
                        true);
                }
                return false;
            }

            MiniGameStageSession session = MiniGameStageCoordinator.Current;
            if (implementation.IsCustom)
            {
                if (!boundToStage || session == null)
                {
                    PatchLog.Error(
                        "机制模块-纯对话/外部 DLL 小游戏只允许在 NPC 社交阶段会话中启动：" +
                        $"minigame={requestedId}, from={_type}, typeId={_typeId}");
                    _fail?.Invoke();
                    return false;
                }

                if (!_registry.OpenRegisteredImplementation(
                        implementation,
                        session,
                        _parms,
                        _type,
                        _typeId,
                        _success,
                        _fail,
                        _result))
                {
                    MiniGameStageCoordinator.AbortCurrentUnsettled(
                        "registered-minigame-open-failed",
                        true);
                    _fail?.Invoke();
                }
                return false;
            }

            OriginalMinigameDescriptor descriptor = implementation.Original;
            __state = new OpenMiniGamePatchState
            {
                ImplementationId = descriptor.Id,
                IsImmediate = descriptor.ImmediateSuccess
            };

            if (boundToStage &&
                logicalId != descriptor.Id &&
                Cfg.MinigameCfgMap.TryGetValue(logicalId, out MinigameCfg logicalCfg) &&
                Cfg.MinigameCfgMap.TryGetValue(descriptor.Id, out MinigameCfg originalCfg))
            {
                // alias 仅在同步 OpenMiniGame 调用期间借用逻辑 ID 的名称/BGM/tips。
                __state.OriginalConfig = originalCfg;
                __state.ConfigWasSwapped = true;
                Cfg.MinigameCfgMap[descriptor.Id] = logicalCfg;
            }

            if (boundToStage && session != null)
            {
                descriptor.BindCallbacks(
                    session.Token,
                    ref _success,
                    ref _fail,
                    ref _result);
            }

            _gameId = descriptor.Id;
            return true;
        }

        internal static void OpenMiniGamePostfix(OpenMiniGamePatchState __state)
        {
            RestoreOriginalConfig(__state);
            if (__state != null && __state.IsImmediate)
            {
                MiniGameStageCoordinator.OnImmediateImplementationFinished();
            }
        }

        internal static Exception OpenMiniGameFinalizer(
            Exception __exception,
            OpenMiniGamePatchState __state)
        {
            RestoreOriginalConfig(__state);
            if (__exception != null)
            {
                MiniGameStageCoordinator.AbortCurrentUnsettled(
                    "open-minigame-exception",
                    true);
            }
            return __exception;
        }

        internal static bool EndGamePrefix(
            MiniGameData __instance,
            ref int _id,
            bool _isWin,
            int _selectId,
            ref EndGamePatchState __state)
        {
            MiniGameStageSession session = MiniGameStageCoordinator.Current;
            if (session == null || !MiniGameStageCoordinator.IsActiveReportId(session, _id))
            {
                return true;
            }

            if (session.Settled)
            {
                return false;
            }

            MiniGameSubData state = MiniGameStateStore.GetOrCreateForNpc(
                __instance,
                session.NpcId,
                session.LogicalGameId);
            __state = new EndGamePatchState
            {
                NpcId = session.NpcId,
                LogicalGameId = session.LogicalGameId,
                IsWin = _isWin,
                BeforeCfgId = state.cfgId,
                Embedded = session.EmbeddedLaunch
            };

            _id = session.LogicalGameId;
            if (session.EmbeddedLaunch)
            {
                __state.SkippedOriginal = true;
                MiniGameStageCoordinator.SettleEmbedded(
                    session.Token,
                    _isWin,
                    _selectId,
                    "MiniGameData.EndGame");
                return false;
            }

            MiniGameStageCoordinator.MarkOriginalSettlementStarted();
            return true;
        }

        internal static void EndGamePostfix(
            MiniGameData __instance,
            EndGamePatchState __state)
        {
            if (__state == null) return;

            if (!__state.SkippedOriginal)
            {
                MiniGameStageCoordinator.MarkOriginalSettlementFinished();
            }

            int afterCfg = MiniGameStateStore.GetCurrentCfgId(
                __instance,
                __state.NpcId,
                __state.LogicalGameId);
            PatchLog.Info(
                "机制模块-NPC 小游戏阶段结算：" +
                $"npc={__state.NpcId}, minigame={__state.LogicalGameId}, " +
                $"win={__state.IsWin}, before={__state.BeforeCfgId}, after={afterCfg}, " +
                $"embedded={__state.Embedded}");
        }

        internal static Exception EndGameFinalizer(
            Exception __exception,
            EndGamePatchState __state)
        {
            if (__exception != null && __state != null)
            {
                MiniGameStageCoordinator.AbortCurrentUnsettled(
                    "end-game-exception",
                    true);
            }
            return __exception;
        }

        internal static void HistoryIdPrefix(ref int _minigameId)
        {
            if (MiniGameStageCoordinator.TryMapReportedGameId(_minigameId, out int logicalId))
            {
                _minigameId = logicalId;
            }
        }

        internal static void ConcreteClosePrefix(
            object __instance,
            ref ConcreteClosePatchState __state)
        {
            MiniGameStageSession session = MiniGameStageCoordinator.Current;
            OriginalMinigameDescriptor descriptor = session?.ActiveImplementation?.Original;
            if (session == null ||
                session.Settled ||
                __instance == null ||
                descriptor == null ||
                !descriptor.NeedsCloseObservation)
            {
                return;
            }

            Type expectedView = descriptor.ResolveViewType();
            if (expectedView == null || !expectedView.IsAssignableFrom(__instance.GetType()))
            {
                return;
            }

            __state = new ConcreteClosePatchState
            {
                Token = session.Token,
                View = __instance,
                Descriptor = descriptor,
                PreviousContext = _closeContext
            };

            if (descriptor.TryReadOutcome(__instance, out bool isWin, out int selectId))
            {
                __state.HasOutcome = true;
                __state.IsWin = isWin;
                __state.SelectId = selectId;
            }

            _closeContext = __state;
        }

        internal static void ConcreteClosePostfix(ConcreteClosePatchState __state)
        {
            if (__state == null) return;

            try
            {
                MiniGameStageSession session = MiniGameStageCoordinator.Current;
                if (session != null && session.Token == __state.Token && !session.Settled)
                {
                    if (__state.HasOutcome)
                    {
                        MiniGameStageCoordinator.CompleteFromAdapter(
                            session.Token,
                            __state.IsWin,
                            __state.SelectId,
                            __state.View.GetType().FullName + ".CloseView");
                    }
                    else if (__state.Descriptor.CompleteOnClose)
                    {
                        MiniGameStageCoordinator.CompleteFromAdapter(
                            session.Token,
                            true,
                            0,
                            __state.View.GetType().FullName + ".CloseView-default-success");
                    }
                }

                MiniGameStageCoordinator.OnConcreteViewClosed(
                    __state.Token,
                    __state.Descriptor.CompleteOnClose);
            }
            finally
            {
                _closeContext = __state.PreviousContext;
            }
        }

        internal static Exception ConcreteCloseFinalizer(
            Exception __exception,
            ConcreteClosePatchState __state)
        {
            if (__state != null)
            {
                _closeContext = __state.PreviousContext;
            }

            if (__exception != null && __state != null)
            {
                MiniGameStageCoordinator.AbortCurrentUnsettled(
                    "concrete-close-exception",
                    true);
            }
            return __exception;
        }

        internal static void ShowTalkPrefix(ref Action _callback)
        {
            if (_closeContext == null || !_closeContext.HasOutcome)
            {
                return;
            }

            MiniGameStageSession session = MiniGameStageCoordinator.Current;
            if (session == null ||
                !session.EmbeddedLaunch ||
                session.Token != _closeContext.Token)
            {
                return;
            }

            if (_callback == null)
            {
                string fieldName = _closeContext.IsWin ? "success" : "fail";
                _callback = ReadCallback(_closeContext.View, fieldName);

                // Talk 内嵌流程有些失败分支仍错误复用了 success 回调。
                if (_callback == null && session.LaunchFrom == MiniGameFromType.Talk)
                {
                    _callback = ReadCallback(_closeContext.View, "success");
                }
            }

            if (_callback == null) return;

            object talkView = UIMgr.GetView("View.Evt.NewTalkView");
            if (talkView == null) return;

            FieldInfo callbackField = AccessTools.Field(talkView.GetType(), "callback");
            if (callbackField != null && callbackField.GetValue(talkView) == null)
            {
                callbackField.SetValue(talkView, _callback);
            }
        }

        private static Action ReadCallback(object view, string fieldName)
        {
            if (view == null || string.IsNullOrWhiteSpace(fieldName)) return null;
            FieldInfo field = AccessTools.Field(view.GetType(), fieldName);
            return field?.GetValue(view) as Action;
        }

        private static void RestoreOriginalConfig(OpenMiniGamePatchState state)
        {
            if (state == null || !state.ConfigWasSwapped || state.OriginalConfig == null)
            {
                return;
            }

            Cfg.MinigameCfgMap[state.ImplementationId] = state.OriginalConfig;
            state.ConfigWasSwapped = false;
        }
    }

    internal sealed class OpenMiniGamePatchState
    {
        internal int ImplementationId;
        internal bool IsImmediate;
        internal bool ConfigWasSwapped;
        internal MinigameCfg OriginalConfig;
    }

    internal sealed class EndGamePatchState
    {
        internal int NpcId;
        internal int LogicalGameId;
        internal bool IsWin;
        internal int BeforeCfgId;
        internal bool Embedded;
        internal bool SkippedOriginal;
    }

    internal sealed class ConcreteClosePatchState
    {
        internal long Token;
        internal object View;
        internal OriginalMinigameDescriptor Descriptor;
        internal bool HasOutcome;
        internal bool IsWin;
        internal int SelectId;
        internal ConcreteClosePatchState PreviousContext;
    }
}
