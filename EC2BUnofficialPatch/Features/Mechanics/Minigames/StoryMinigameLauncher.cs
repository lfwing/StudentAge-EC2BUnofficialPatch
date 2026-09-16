using System;
using System.Collections.Generic;
using EC2BUnofficialPatch.Core;
using Sdk;

namespace EC2BUnofficialPatch.Features.Mechanics.Minigames
{
    /// <summary>
    /// 普通剧情（非 NPC 社交阶段）里通过 TalkCfg/OptionCfg 的 miniGame 字段打开
    /// dialogue/external 自定义小游戏。
    ///
    /// 与社交阶段会话不同，这里没有 NPC 阶段、不扣消耗、不回写 MiniGameSubData、
    /// 不发放阶段奖励：小游戏只负责回传胜负，剧情分支由原版传入的
    /// success/fail/result 回调继续（成功走 nextTalk/talkId，失败走 nextTalk2/talkId2）。
    /// 回调语义与内嵌到社交阶段时保持一致。
    /// </summary>
    internal static class StoryMinigameLauncher
    {
        private sealed class StoryLaunch
        {
            internal long Token;
            internal int GameId;
            internal MiniGameFromType From;
            internal int SourceId;
            internal TheEntity.Role Player;
            internal bool SeenRunningWorld;
            internal bool Finished;
            internal CustomMinigameContext Context;
        }

        private static long _nextToken;
        private static StoryLaunch _current;

        /// <summary>当前是否有一个尚未结束的剧情小游戏。</summary>
        internal static bool IsBusy => _current != null && !_current.Finished;

        /// <summary>
        /// 尝试在剧情里打开自定义小游戏。返回 false 表示没有打开（调用方负责 fail 回调）。
        /// </summary>
        internal static bool TryOpen(
            ResolvedMinigameImplementation implementation,
            int requestedGameId,
            MiniGameFromType fromType,
            int typeId,
            IReadOnlyList<double> launchParameters,
            Action success,
            Action fail,
            Action<float> result)
        {
            if (implementation == null || implementation.Definition == null || !implementation.IsCustom)
            {
                return false;
            }

            if (fromType != MiniGameFromType.Talk && fromType != MiniGameFromType.Option)
            {
                PatchLog.Error(
                    "机制模块-剧情小游戏只能由 Talk/Option 的 miniGame 字段打开：" +
                    $"minigame={requestedGameId}, from={fromType}, typeId={typeId}");
                return false;
            }

            if (IsBusy)
            {
                PatchLog.Warning(
                    "机制模块-已有剧情小游戏进行中，拒绝第二次启动：" +
                    $"running={_current.GameId}, requested={requestedGameId}, from={fromType}, typeId={typeId}");
                return false;
            }

            CustomMinigameDefinition definition = implementation.Definition;
            StoryLaunch launch = new StoryLaunch
            {
                Token = ++_nextToken,
                GameId = requestedGameId,
                From = fromType,
                SourceId = typeId,
                Player = Singleton<RoleMgr>.Ins.GetRole(),
                SeenRunningWorld = Game.GetGameState() != GameState.Start
            };
            _current = launch;

            Action<bool, int> complete = (isWin, selectId) =>
            {
                if (_current != launch || launch.Finished) return;
                launch.Finished = true;
                PatchLog.Info(
                    "机制模块-剧情小游戏结束：" +
                    $"minigame={launch.GameId}, from={launch.From}, typeId={launch.SourceId}, " +
                    $"win={isWin}, selectId={selectId}");
                _current = null;
                InvokeFlowCallbacks(launch.From, isWin, selectId, success, fail, result);
            };

            if (definition.Kind == CustomMinigameKind.Dialogue)
            {
                // 纯对话实现没有玩法，剧情里直接按成功继续。
                PatchLog.Info(
                    $"机制模块-剧情中的纯对话小游戏直接成功：minigame={requestedGameId}, from={fromType}, typeId={typeId}");
                complete(true, 0);
                return true;
            }

            if (definition.Kind != CustomMinigameKind.External || definition.ExternalType == null)
            {
                _current = null;
                return false;
            }

            try
            {
                ICustomMinigame instance =
                    (ICustomMinigame)Activator.CreateInstance(definition.ExternalType);
                CustomMinigameContext context = new CustomMinigameContext(
                    requestedGameId,
                    0,
                    0,
                    definition.SourceFile,
                    definition.Parameters,
                    launchParameters,
                    fromType,
                    typeId,
                    complete);
                launch.Context = context;
                context.BindLifecycle(
                    // 剧情启动不扣消耗，Begin 只确认会话仍然有效。
                    () => _current == launch && !launch.Finished,
                    () => _current == launch && !launch.Finished,
                    () =>
                    {
                        if (_current != launch || launch.Finished) return;
                        launch.Finished = true;
                        _current = null;
                        PatchLog.Info(
                            $"机制模块-剧情小游戏被取消：minigame={launch.GameId}, from={launch.From}, typeId={launch.SourceId}");
                        // 取消不算胜利：优先走失败分支，让剧情能继续。
                        if (fail != null) fail();
                        else if (success != null) success();
                        else result?.Invoke(0f);
                    });
                instance.Open(context);
                PatchLog.Info(
                    "机制模块-已在剧情中调用外部小游戏：" +
                    $"minigame={requestedGameId}, from={fromType}, typeId={typeId}, " +
                    $"parms={launchParameters?.Count ?? 0}, type={definition.TypeName}");
                return true;
            }
            catch (Exception exception)
            {
                if (_current == launch) _current = null;
                launch.Finished = true;
                PatchLog.Exception(
                    $"机制模块-剧情中打开外部小游戏异常：minigame={requestedGameId}, type={definition.TypeName}",
                    exception);
                return false;
            }
        }

        /// <summary>读档或回到主菜单时撤销尚未结束的剧情小游戏。</summary>
        internal static void Tick()
        {
            StoryLaunch launch = _current;
            if (launch == null || launch.Finished) return;
            try
            {
                GameState state = Game.GetGameState();
                if (state != GameState.Start) launch.SeenRunningWorld = true;
                if ((launch.SeenRunningWorld && state == GameState.Start) ||
                    !ReferenceEquals(launch.Player, Singleton<RoleMgr>.Ins.GetRole()))
                {
                    Clear(launch, "world-changed");
                }
            }
            catch
            {
                Clear(launch, "world-unavailable");
            }
        }

        private static void Clear(StoryLaunch launch, string reason)
        {
            if (_current != launch) return;
            launch.Finished = true;
            _current = null;
            PatchLog.Debug(
                $"机制模块-清理剧情小游戏：reason={reason}, minigame={launch.GameId}, from={launch.From}, typeId={launch.SourceId}");
            // 世界已切换，原剧情回调不再有意义，只通知外部释放界面。
            launch.Context?.Invalidate();
        }

        private static void InvokeFlowCallbacks(
            MiniGameFromType from,
            bool isWin,
            int selectId,
            Action success,
            Action fail,
            Action<float> result)
        {
            try
            {
                if (isWin)
                {
                    success?.Invoke();
                }
                else if (fail != null)
                {
                    fail();
                }
                else if (from == MiniGameFromType.Talk || from == MiniGameFromType.Option)
                {
                    // 原版 Talk 内嵌只提供 success 槽作为流程完成 callback；失败时仍要继续剧情。
                    success?.Invoke();
                }

                if (success == null && fail == null)
                {
                    result?.Invoke(selectId);
                }
            }
            catch (Exception exception)
            {
                PatchLog.Exception("机制模块-剧情小游戏回调剧情分支时异常", exception);
            }
        }
    }
}
