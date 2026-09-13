using System;
using System.Collections.Generic;
using Config;
using Sdk;

namespace EC2BUnofficialPatch.Features.Mechanics.Minigames
{
    /// <summary>
    /// 原版小游戏的 Level 启动契约。这里不再把“FuncMgr switch 中存在分支”
    /// 等同于“可以脱离原入口作为 NPC 社交阶段后备运行”。
    /// </summary>
    internal sealed class OriginalMinigameLaunchAdapter
    {
        private readonly Func<MinigameActionCfg, string> _validateLevel;
        private readonly Action<MiniGameStageSession> _openLevel;

        internal OriginalMinigameLaunchAdapter(
            int id,
            bool supportsLevelFallback,
            Func<MinigameActionCfg, string> validateLevel = null,
            Action<MiniGameStageSession> openLevel = null)
        {
            Id = id;
            SupportsLevelFallback = supportsLevelFallback;
            _validateLevel = validateLevel;
            _openLevel = openLevel;
        }

        internal int Id { get; }
        internal bool SupportsLevelFallback { get; }

        internal bool TryValidateLevel(MinigameActionCfg action, out string error)
        {
            error = null;
            if (!SupportsLevelFallback)
            {
                error = "该原版玩法没有独立可靠的 Level 启动契约，必须从原版 Talk/Option/Evt 入口内嵌启动";
                return false;
            }

            if (action == null)
            {
                error = "MinigameActionCfg 为空";
                return false;
            }

            error = _validateLevel?.Invoke(action);
            return string.IsNullOrWhiteSpace(error);
        }

        internal void OpenLevel(MiniGameStageSession session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            if (!SupportsLevelFallback || _openLevel == null)
                throw new InvalidOperationException($"小游戏没有 Level 启动处理器：id={Id}");
            _openLevel(session);
        }
    }

    internal static class OriginalMinigameAdapterRegistry
    {
        private static readonly Dictionary<int, OriginalMinigameLaunchAdapter> Adapters =
            new Dictionary<int, OriginalMinigameLaunchAdapter>
            {
                // 已逐项核对 1.93 View.OnOpen/CloseView：这些玩法具备 Level + EndGame 协议。
                { 3,  Level(3, ValidateDivination, OpenDivination) },
                { 5,  Level(5, ValidateSudoku, OpenSudoku) },
                { 9,  Level(9, ValidatePuzzle, OpenPuzzle) },
                { 10, Level(10, ValidateBrick, OpenBrick) },
                { 20, Level(20, ValidateFingerKnife, OpenFingerKnife) },
                { 24, Level(24, ValidatePiano, OpenPiano) },
                { 26, Level(26, ValidateBadminton, OpenBadminton26) },
                { 28, Level(28, ValidateImmediate, OpenImmediate28) },
                { 32, Level(32, ValidateBadminton, OpenBadminton32) },
                { 41, Level(41, ValidateMusic, OpenMusic) },
                { 43, Level(43, ValidateFishing, OpenFishing) },
                { 46, Level(46, ValidateWeaving, OpenWeaving) },
                { 48, Level(48, ValidateDrawing, OpenDrawing) },

                // 这些玩法依赖原版 Talk/Option/Evt/Match/Love 上下文、回调或额外对象。
                // 它们仍可被 startTalk 内嵌调用，但绝不再用空参数强制作为 Level 后备打开。
                { 1, EmbeddedOnly(1) },
                { 2, EmbeddedOnly(2) },
                { 4, EmbeddedOnly(4) },
                { 6, EmbeddedOnly(6) },
                { 7, EmbeddedOnly(7) },
                { 8, EmbeddedOnly(8) },
                { 11, EmbeddedOnly(11) },
                { 13, EmbeddedOnly(13) },
                { 14, EmbeddedOnly(14) },
                { 15, EmbeddedOnly(15) },
                { 16, EmbeddedOnly(16) },
                { 17, EmbeddedOnly(17) },
                { 18, EmbeddedOnly(18) },
                { 19, EmbeddedOnly(19) },
                { 21, EmbeddedOnly(21) },
                { 22, EmbeddedOnly(22) },
                { 23, EmbeddedOnly(23) },
                { 27, EmbeddedOnly(27) },
                { 29, EmbeddedOnly(29) },
                { 30, EmbeddedOnly(30) },
                { 31, EmbeddedOnly(31) },
                { 33, EmbeddedOnly(33) },
                { 34, EmbeddedOnly(34) },
                { 35, EmbeddedOnly(35) },
                { 36, EmbeddedOnly(36) },
                { 37, EmbeddedOnly(37) },
                { 39, EmbeddedOnly(39) },
                { 42, EmbeddedOnly(42) },
                { 44, EmbeddedOnly(44) },
                { 45, EmbeddedOnly(45) },
                { 47, EmbeddedOnly(47) }
            };

        internal static IEnumerable<OriginalMinigameLaunchAdapter> All => Adapters.Values;

        internal static bool TryGet(int id, out OriginalMinigameLaunchAdapter adapter) =>
            Adapters.TryGetValue(id, out adapter);

        internal static OriginalMinigameLaunchAdapter Require(int id)
        {
            if (!Adapters.TryGetValue(id, out OriginalMinigameLaunchAdapter adapter))
                throw new KeyNotFoundException($"缺少原版小游戏启动适配器：id={id}");
            return adapter;
        }

        private static OriginalMinigameLaunchAdapter Level(
            int id,
            Func<MinigameActionCfg, string> validate,
            Action<MiniGameStageSession> open) =>
            new OriginalMinigameLaunchAdapter(id, true, validate, open);

        private static OriginalMinigameLaunchAdapter EmbeddedOnly(int id) =>
            new OriginalMinigameLaunchAdapter(id, false);

        private static void OpenDivination(MiniGameStageSession session)
        {
            EnsureImplementation(session, 3);
            Singleton<FuncMgr>.Ins.OpenMiniGame(
                3, MiniGameFromType.Level, session.CfgId,
                null, null, null, null, 0);
        }

        private static void OpenSudoku(MiniGameStageSession session)
        {
            EnsureImplementation(session, 5);
            Singleton<FuncMgr>.Ins.OpenMiniGame(
                5, MiniGameFromType.Level, session.CfgId,
                null, null, null, null, 0);
        }

        private static void OpenPuzzle(MiniGameStageSession session)
        {
            EnsureImplementation(session, 9);
            Singleton<FuncMgr>.Ins.OpenMiniGame(
                9, MiniGameFromType.Level, session.CfgId,
                null, null, null, null, 0);
        }

        private static void OpenBrick(MiniGameStageSession session)
        {
            EnsureImplementation(session, 10);
            Singleton<FuncMgr>.Ins.OpenMiniGame(
                10, MiniGameFromType.Level, session.CfgId,
                null, null, null, null, 0);
        }

        private static void OpenFingerKnife(MiniGameStageSession session)
        {
            EnsureImplementation(session, 20);
            Singleton<FuncMgr>.Ins.OpenMiniGame(
                20, MiniGameFromType.Level, session.CfgId,
                null, null, null, null, 0);
        }

        private static void OpenPiano(MiniGameStageSession session)
        {
            EnsureImplementation(session, 24);
            Singleton<FuncMgr>.Ins.OpenMiniGame(
                24, MiniGameFromType.Level, session.CfgId,
                null, null, null, null, 0);
        }

        private static void OpenBadminton26(MiniGameStageSession session)
        {
            EnsureImplementation(session, 26);
            Singleton<FuncMgr>.Ins.OpenMiniGame(
                26, MiniGameFromType.Level, session.CfgId,
                null, null, null, null, 0);
        }

        private static void OpenImmediate28(MiniGameStageSession session)
        {
            EnsureImplementation(session, 28);
            Singleton<FuncMgr>.Ins.OpenMiniGame(
                28, MiniGameFromType.Level, session.CfgId,
                null, null, null, null, 0);
        }

        private static void OpenBadminton32(MiniGameStageSession session)
        {
            EnsureImplementation(session, 32);
            Singleton<FuncMgr>.Ins.OpenMiniGame(
                32, MiniGameFromType.Level, session.CfgId,
                null, null, null, null, 0);
        }

        private static void OpenMusic(MiniGameStageSession session)
        {
            EnsureImplementation(session, 41);
            Singleton<FuncMgr>.Ins.OpenMiniGame(
                41, MiniGameFromType.Level, session.CfgId,
                null, null, null, null, 0);
        }

        private static void OpenFishing(MiniGameStageSession session)
        {
            EnsureImplementation(session, 43);
            Singleton<FuncMgr>.Ins.OpenMiniGame(
                43, MiniGameFromType.Level, session.CfgId,
                null, null, null, null, 0);
        }

        private static void OpenWeaving(MiniGameStageSession session)
        {
            EnsureImplementation(session, 46);
            Singleton<FuncMgr>.Ins.OpenMiniGame(
                46, MiniGameFromType.Level, session.CfgId,
                null, null, null, null, 0);
        }

        private static void OpenDrawing(MiniGameStageSession session)
        {
            EnsureImplementation(session, 48);
            Singleton<FuncMgr>.Ins.OpenMiniGame(
                48, MiniGameFromType.Level, session.CfgId,
                null, null, null, null, 0);
        }

        private static void EnsureImplementation(
            MiniGameStageSession session,
            int expectedImplementationId)
        {
            int actual = session?.FallbackImplementation?.ImplementationId ?? 0;
            if (actual != expectedImplementationId)
                throw new InvalidOperationException(
                    $"启动适配器与实际实现不匹配：expected={expectedImplementationId}, actual={actual}");

            // FuncMgr 只执行该玩法原版分支；当前适配器独立负责启动前提与结算归属。
        }

        private static string ValidateDivination(MinigameActionCfg action) =>
            Cfg.DivinationCfgMap == null || Cfg.DivinationCfgMap.Count == 0
                ? "DivinationCfg 为空"
                : null;

        private static string ValidateSudoku(MinigameActionCfg action)
        {
            int emptyCount = action.mode;
            if (emptyCount == 0)
            {
                if (Cfg.PersonConstCfgMap == null || !Cfg.PersonConstCfgMap.ContainsKey(5001))
                    return "mode=0，但缺少默认难度 PersonConstCfg[5001]";
                return null;
            }

            return emptyCount < 1 || emptyCount > 36
                ? $"数独 mode 必须是 1～36 的挖空格数，actual={emptyCount}"
                : null;
        }

        private static string ValidatePuzzle(MinigameActionCfg action)
        {
            if (Cfg.PuzzleMinigameCfgMap == null || Cfg.PuzzleMinigameCfgMap.Count == 0)
                return "PuzzleMinigameCfg 为空";
            if (action.mode == 0)
                return null;
            if (!Cfg.PuzzleMinigameCfgMap.TryGetValue(action.mode, out PuzzleMinigameCfg cfg))
                return $"mode={action.mode} 不存在于 PuzzleMinigameCfg";
            if (cfg.size == null || cfg.size.Count < 2 || cfg.size[0] <= 0 || cfg.size[1] <= 0)
                return $"PuzzleMinigameCfg[{action.mode}].size 无效";
            return null;
        }

        private static string ValidateBrick(MinigameActionCfg action) =>
            Cfg.BrickMinigameCfgMap == null || !Cfg.BrickMinigameCfgMap.ContainsKey(action.mode)
                ? $"mode={action.mode} 不存在于 BrickMinigameCfg"
                : null;

        private static string ValidateFingerKnife(MinigameActionCfg action)
        {
            if (action.parms == null || action.parms.Count < 3)
                return "戳指缝要求 parms=[AI最短反应时间, AI最长反应时间, AI成功率, 可选错误震动时间]";
            if (action.parms[0] <= 0f || action.parms[1] < action.parms[0])
                return $"戳指缝 AI 时间范围无效：min={action.parms[0]}, max={action.parms[1]}";
            if (action.parms[2] < 0f || action.parms[2] > 1f)
                return $"戳指缝 AI 成功率必须在 0～1：actual={action.parms[2]}";
            return null;
        }

        private static string ValidatePiano(MinigameActionCfg action)
        {
            if (Cfg.PianoCfgMap == null || !Cfg.PianoCfgMap.TryGetValue(action.mode, out PianoCfg piano))
                return $"mode={action.mode} 不存在于 PianoCfg";
            if (piano.datas == null || piano.datas.Count == 0)
                return $"PianoCfg[{action.mode}].datas 为空";
            if (Cfg.PianoKeyCfgMap == null || Cfg.PianoKeyCfgMap.Count == 0)
                return "PianoKeyCfg 为空";

            foreach (List<int> note in piano.datas)
            {
                if (note == null || note.Count == 0)
                    return $"PianoCfg[{action.mode}] 包含空音符";
                int keyId = (int)note[0];
                if (!Cfg.PianoKeyCfgMap.ContainsKey(keyId))
                    return $"PianoCfg[{action.mode}] 引用了不存在的 PianoKeyCfg[{keyId}]";
            }
            return null;
        }

        private static string ValidateBadminton(MinigameActionCfg action) =>
            Cfg.BadmintonLevelCfgMap == null || !Cfg.BadmintonLevelCfgMap.ContainsKey(action.mode)
                ? $"mode={action.mode} 不存在于 BadmintonLevelCfg"
                : null;

        private static string ValidateImmediate(MinigameActionCfg action) => null;

        private static string ValidateMusic(MinigameActionCfg action) =>
            Cfg.MusicMinigameCfgMap == null || !Cfg.MusicMinigameCfgMap.ContainsKey(action.mode)
                ? $"mode={action.mode} 不存在于 MusicMinigameCfg"
                : null;

        private static string ValidateFishing(MinigameActionCfg action) => null;

        private static string ValidateWeaving(MinigameActionCfg action) =>
            Cfg.WeavingMinigameCfgMap == null || !Cfg.WeavingMinigameCfgMap.ContainsKey(action.mode)
                ? $"mode={action.mode} 不存在于 WeavingMinigameCfg"
                : null;

        private static string ValidateDrawing(MinigameActionCfg action) =>
            Cfg.DrawingMinigameCfgMap == null || !Cfg.DrawingMinigameCfgMap.ContainsKey(action.mode)
                ? $"mode={action.mode} 不存在于 DrawingMinigameCfg"
                : null;
    }
}
