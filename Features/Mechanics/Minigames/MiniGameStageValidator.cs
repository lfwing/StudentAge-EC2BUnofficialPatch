using System;
using System.Collections.Generic;
using System.Linq;
using Config;
using EC2BUnofficialPatch.Core;
using Sdk;

namespace EC2BUnofficialPatch.Features.Mechanics.Minigames
{
    internal sealed class EmbeddedMinigameLaunchPoint : IEquatable<EmbeddedMinigameLaunchPoint>
    {
        internal EmbeddedMinigameLaunchPoint(
            MiniGameFromType fromType,
            int typeId,
            int gameId)
        {
            FromType = fromType;
            TypeId = typeId;
            GameId = gameId;
        }

        internal MiniGameFromType FromType { get; }
        internal int TypeId { get; }
        internal int GameId { get; }

        public bool Equals(EmbeddedMinigameLaunchPoint other) =>
            other != null &&
            FromType == other.FromType &&
            TypeId == other.TypeId &&
            GameId == other.GameId;

        public override bool Equals(object obj) =>
            Equals(obj as EmbeddedMinigameLaunchPoint);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)FromType;
                hash = (hash * 397) ^ TypeId;
                hash = (hash * 397) ^ GameId;
                return hash;
            }
        }

        public override string ToString() =>
            $"{FromType}:{TypeId}->{GameId}";
    }

    internal sealed class MiniGameStagePlan
    {
        internal MinigameActionCfg Action;
        internal ResolvedMinigameImplementation Fallback;
        internal HashSet<EmbeddedMinigameLaunchPoint> EmbeddedLaunches =
            new HashSet<EmbeddedMinigameLaunchPoint>();
    }

    internal static class MiniGameStageValidator
    {
        private static bool _definitionValidationCompleted;

        internal static void ResetDefinitionValidation()
        {
            _definitionValidationCompleted = false;
        }

        /// <summary>
        /// 尝试输出一次全局阶段定义诊断。游戏 cfg 在 BepInEx Awake 时可能尚未创建，
        /// 因此该方法必须允许延迟重试，且任何诊断异常都不能阻止核心 Harmony 补丁加载。
        /// </summary>
        internal static bool TryLogDefinitionProblems(CustomMinigameRegistry registry)
        {
            if (_definitionValidationCompleted)
            {
                return true;
            }

            IDictionary<int, MinigameActionCfg> actionMap = Cfg.MinigameActionCfgMap;
            IDictionary<int, MinigameCfg> gameMap = Cfg.MinigameCfgMap;
            if (actionMap == null || gameMap == null)
            {
                return false;
            }

            try
            {
                Dictionary<int, SortedSet<int>> groups =
                    new Dictionary<int, SortedSet<int>>();
                foreach (MinigameActionCfg cfg in actionMap.Values)
                {
                    if (cfg == null || cfg.id <= 0) continue;
                    int logicalId = cfg.id / 100;
                    int stage = cfg.id % 100;
                    if (!groups.TryGetValue(logicalId, out SortedSet<int> stages))
                    {
                        stages = new SortedSet<int>();
                        groups[logicalId] = stages;
                    }
                    stages.Add(stage);
                }

                PatchLog.Registration(
                    $"机制模块-开始检查 MinigameActionCfg 阶段定义：count={actionMap.Count}");

                foreach (KeyValuePair<int, SortedSet<int>> pair in groups.OrderBy(p => p.Key))
                {
                    int logicalId = pair.Key;
                    SortedSet<int> stages = pair.Value;
                    if (logicalId <= 0 || stages.Count == 0)
                    {
                        PatchLog.Warning(
                            $"机制模块-忽略无法识别的阶段组：minigame={logicalId}, stages={FormatStages(stages)}");
                        continue;
                    }

                    if (!gameMap.ContainsKey(logicalId))
                    {
                        PatchLog.Warning(
                            "机制模块-阶段配置缺少对应 MinigameCfg：" +
                            $"minigame={logicalId}, stages={FormatStages(stages)}");
                    }

                    int max = stages.Max;
                    List<int> missing = Enumerable.Range(1, max)
                        .Where(stage => !stages.Contains(stage))
                        .ToList();
                    if (!stages.Contains(1) || missing.Count > 0)
                    {
                        PatchLog.Warning(
                            "机制模块-小游戏阶段不是从 01 开始连续排列；" +
                            "原版推进只会 level++，缺号会被直接判定为完成：" +
                            $"minigame={logicalId}, stages={FormatStages(stages)}, " +
                            $"missing={FormatStages(missing)}");
                    }

                    if (max >= 99)
                    {
                        PatchLog.Warning(
                            "机制模块-小游戏阶段已使用 99；原版 cfgId 编码无法安全表达第 100 阶段：" +
                            $"minigame={logicalId}");
                    }

                    if (!OriginalMinigameCatalog.HasDispatcher(logicalId) &&
                        (registry == null || !registry.HasExplicitMapping(logicalId)))
                    {
                        PatchLog.Registration(
                            "机制模块-发现自定义逻辑小游戏 ID；其实现可由 startTalk 内嵌玩法优先提供，" +
                            "也可由 CustomMinigamecfg.json 提供后备实现：" +
                            $"minigame={logicalId}, stages={FormatStages(stages)}");
                    }
                }

                _definitionValidationCompleted = true;
                return true;
            }
            catch (Exception exception)
            {
                PatchLog.Exception(
                    "机制模块-阶段定义诊断发生异常；核心小游戏补丁继续工作",
                    exception);
                return false;
            }
        }

        internal static bool TryValidateCurrentStage(
            int npcId,
            MiniGameSubData state,
            CustomMinigameRegistry registry,
            out MiniGameStagePlan plan,
            out string error)
        {
            plan = null;
            error = null;

            if (registry == null)
            {
                error = "自定义小游戏注册表尚未初始化";
                return false;
            }

            if (Cfg.MinigameCfgMap == null || Cfg.MinigameActionCfgMap == null)
            {
                error = "游戏小游戏 cfg 尚未初始化，请在主菜单加载完成后重试";
                return false;
            }

            if (state == null)
            {
                error = $"NPC 小游戏状态为空：npc={npcId}";
                return false;
            }

            if (!Cfg.MinigameCfgMap.ContainsKey(state.id))
            {
                error = $"缺少 MinigameCfg：npc={npcId}, minigame={state.id}";
                return false;
            }

            if (!Cfg.MinigameActionCfgMap.TryGetValue(state.cfgId, out MinigameActionCfg action))
            {
                error =
                    "当前阶段不存在，小游戏已完成或 cfg 阶段不连续：" +
                    $"npc={npcId}, minigame={state.id}, cfg={state.cfgId}";
                return false;
            }

            if (action.id / 100 != state.id || action.id % 100 <= 0)
            {
                error =
                    "MinigameActionCfg.id 不符合“小游戏ID*100+阶段”规则：" +
                    $"npc={npcId}, minigame={state.id}, cfg={action.id}";
                return false;
            }

            EmbeddedScanResult embedded = ScanEmbeddedMinigames(action.startTalk, registry);
            if (!embedded.IsValid)
            {
                error =
                    "阶段对话包含不能绑定到社交阶段的内嵌小游戏：" +
                    $"npc={npcId}, minigame={state.id}, cfg={state.cfgId}, " +
                    $"reason={embedded.Error}";
                return false;
            }

            ResolvedMinigameImplementation fallback = null;
            if (registry.TryResolve(state.id, out ResolvedMinigameImplementation resolvedFallback))
            {
                if (resolvedFallback.CanOpenAsFallback)
                {
                    fallback = resolvedFallback;
                }
                else if (embedded.Launches.Count == 0)
                {
                    error =
                        "该玩法依赖 Option/Talk 上下文或额外启动参数，不能在 startTalk " +
                        "结束后以 Level+空参数自动打开；请在 startTalk 中通过 Talk/Option miniGame 内嵌打开：" +
                        $"npc={npcId}, minigame={state.id}, implementation={resolvedFallback.ImplementationId}, " +
                        $"cfg={state.cfgId}";
                    return false;
                }
            }

            if (fallback == null && embedded.Launches.Count == 0)
            {
                error =
                    "逻辑小游戏没有可用实现。请优先在 startTalk 的 Talk/Option miniGame 中内嵌玩法，" +
                    "或在 CustomMinigamecfg.json 中提供 direct、alias、dialogue、external 后备实现：" +
                    $"npc={npcId}, minigame={state.id}, cfg={state.cfgId}";
                return false;
            }

            if (embedded.Launches.Count > 0 && fallback == null)
            {
                PatchLog.Registration(
                    "机制模块-当前阶段完全依赖 startTalk 内嵌小游戏；" +
                    "请确保玩家可到达的每条结束分支都会实际打开一个内嵌玩法：" +
                    $"npc={npcId}, minigame={state.id}, cfg={state.cfgId}, " +
                    $"embedded={FormatEmbedded(embedded.Launches)}");
            }

            if (fallback?.Original?.Category == SocialMinigameCategory.SpecialCompleteOnClose)
            {
                PatchLog.Warning(
                    "机制模块-当前社交阶段使用特殊/情侣玩法作为后备；" +
                    "该玩法没有统一社交胜负协议，正常关闭且未报告结果时将按成功推进：" +
                    $"npc={npcId}, logical={state.id}, implementation={fallback.ImplementationId}, cfg={state.cfgId}");
            }

            plan = new MiniGameStagePlan
            {
                Action = action,
                Fallback = fallback,
                EmbeddedLaunches = embedded.Launches
            };
            return true;
        }

        private static EmbeddedScanResult ScanEmbeddedMinigames(
            int startTalk,
            CustomMinigameRegistry registry)
        {
            EmbeddedScanResult result = new EmbeddedScanResult();
            if (startTalk <= 0)
            {
                return result;
            }

            if (Cfg.TalkCfgMap == null || Cfg.OptionCfgMap == null)
            {
                result.IsValid = false;
                result.Error = "TalkCfg/OptionCfg 尚未初始化";
                return result;
            }

            Queue<int> pending = new Queue<int>();
            HashSet<int> visitedTalks = new HashSet<int>();
            pending.Enqueue(startTalk);

            while (pending.Count > 0 && visitedTalks.Count < 1000)
            {
                int talkId = pending.Dequeue();
                if (talkId <= 0 || !visitedTalks.Add(talkId))
                {
                    continue;
                }

                if (!Cfg.TalkCfgMap.TryGetValue(talkId, out TalkCfg talk))
                {
                    continue;
                }

                if (!CheckMiniGameList(
                        talk.miniGame,
                        MiniGameFromType.Talk,
                        talk.id,
                        registry,
                        result))
                {
                    return result;
                }

                EnqueueTalks(talk.nextTalk, pending);
                EnqueueTalks(talk.nextTalk2, pending);

                if (talk.option == null)
                {
                    continue;
                }

                foreach (int optionId in talk.option)
                {
                    if (!Cfg.OptionCfgMap.TryGetValue(optionId, out OptionCfg option))
                    {
                        continue;
                    }

                    if (!CheckMiniGameList(
                            option.miniGame,
                            MiniGameFromType.Option,
                            option.id,
                            registry,
                            result))
                    {
                        return result;
                    }

                    EnqueueTalks(option.talkId, pending);
                    EnqueueTalks(option.talkId2, pending);
                }
            }

            if (pending.Count > 0)
            {
                PatchLog.Warning(
                    "机制模块-阶段对话图超过 1000 个 Talk，内嵌小游戏检查提前停止：" +
                    $"startTalk={startTalk}");
            }

            return result;
        }

        private static bool CheckMiniGameList(
            List<double> miniGame,
            MiniGameFromType fromType,
            int typeId,
            CustomMinigameRegistry registry,
            EmbeddedScanResult result)
        {
            if (miniGame == null || miniGame.Count == 0)
            {
                return true;
            }

            int requestedId = (int)miniGame[0];
            if (!registry.TryResolve(requestedId, out ResolvedMinigameImplementation implementation))
            {
                result.IsValid = false;
                result.Error =
                    $"无法分发内嵌小游戏 {requestedId}（{fromType}:{typeId}）";
                return false;
            }

            if (!implementation.CanOpenEmbedded)
            {
                result.IsValid = false;
                result.Error =
                    implementation.Kind == CustomMinigameKind.Dialogue
                        ? $"纯对话实现 {requestedId} 不能内嵌；它没有 View 可恢复被隐藏的 Talk/Option"
                        : $"内嵌小游戏 {requestedId} 是无 View 的立即结算玩法";
                return false;
            }

            result.Launches.Add(
                new EmbeddedMinigameLaunchPoint(fromType, typeId, requestedId));
            return true;
        }

        private static void EnqueueTalks(IEnumerable<int> ids, Queue<int> pending)
        {
            if (ids == null) return;
            foreach (int id in ids)
            {
                if (id > 0) pending.Enqueue(id);
            }
        }

        private static string FormatStages(IEnumerable<int> stages)
        {
            if (stages == null) return "无";
            string text = string.Join(",", stages.Select(stage => stage.ToString("00")));
            return string.IsNullOrEmpty(text) ? "无" : text;
        }

        private static string FormatEmbedded(
            IEnumerable<EmbeddedMinigameLaunchPoint> launches)
        {
            if (launches == null) return "无";
            string text = string.Join(",", launches.Select(item => item.ToString()));
            return string.IsNullOrWhiteSpace(text) ? "无" : text;
        }

        private sealed class EmbeddedScanResult
        {
            internal bool IsValid = true;
            internal string Error;
            internal HashSet<EmbeddedMinigameLaunchPoint> Launches { get; } =
                new HashSet<EmbeddedMinigameLaunchPoint>();
        }
    }
}
