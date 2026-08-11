using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace EC2BUnofficialPatch.Features.Mechanics.Minigames
{
    internal sealed class MiniGameTemplateInfo
    {
        internal MiniGameTemplateInfo(
            int id,
            bool supportsLevelProgression,
            string viewTypeName = null,
            string outcomeField = null,
            string winEnumName = null,
            string selectIdField = null)
        {
            Id = id;
            SupportsLevelProgression = supportsLevelProgression;
            ViewTypeName = viewTypeName;
            OutcomeField = outcomeField;
            WinEnumName = winEnumName;
            SelectIdField = selectIdField;
        }

        internal int Id { get; }
        internal bool SupportsLevelProgression { get; }
        internal string ViewTypeName { get; }
        internal string OutcomeField { get; }
        internal string WinEnumName { get; }
        internal string SelectIdField { get; }

        internal Type ResolveViewType() =>
            string.IsNullOrWhiteSpace(ViewTypeName)
                ? null
                : AccessTools.TypeByName(ViewTypeName);
    }

    internal static class MiniGameTemplateCatalog
    {
        // 与 1.93 FuncMgr.OpenMiniGame 的 switch 一一对应。
        private static readonly int[] DispatcherIds =
        {
            1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11,
            13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24,
            26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37,
            39, 41, 42, 43, 44, 45, 46, 47, 48
        };

        private static readonly Dictionary<int, MiniGameTemplateInfo> Templates =
            DispatcherIds.ToDictionary(
                id => id,
                id => CreateFromAdapter(id));

        private static readonly IReadOnlyList<MiniGameTemplateInfo> StageViews =
            new[]
            {
                RegisterStageView(
                    3,
                    "MiniGame.Divination.DivinationMiniGameView",
                    "agree",
                    null,
                    "selectId"),
                RegisterStageView(
                    5,
                    "MiniGame.Sudoku.SudokuMiniGameView",
                    "curState",
                    "Win"),
                RegisterStageView(
                    9,
                    "MiniGame.Puzzle.PuzzleMiniGameView",
                    "curState",
                    "Win"),
                RegisterStageView(
                    10,
                    "MiniGame.Other.BrickGameView",
                    "isWin"),
                RegisterStageView(
                    20,
                    "MiniGame.FingerKnife.FingerKnifeMiniGameView",
                    "isWin"),
                RegisterStageView(
                    24,
                    "MiniGame.Piano.PianoMiniGameView",
                    "isWin"),
                RegisterStageView(
                    26,
                    "MiniGame.Badminton.BadmintonMiniGameView",
                    "curState",
                    "Win"),
                RegisterStageView(
                    41,
                    "MiniGame.Music.MusicMiniGameView",
                    "curState",
                    "Win"),
                RegisterStageView(
                    43,
                    "MiniGame.Fishing.FishingMiniGameView",
                    "curState",
                    "Win"),
                RegisterStageView(
                    46,
                    "MiniGame.Weaving.WeavingMinigameView",
                    "curState",
                    "Win"),
                RegisterStageView(
                    48,
                    "MiniGame.Drawing.DrawingMinigameView",
                    "curState",
                    "Win")
            };

        static MiniGameTemplateCatalog()
        {
            // 32 与 26 复用同一个羽毛球 View，原 View 的 EndGame/历史 ID 写死为 26。
            Templates[32] = new MiniGameTemplateInfo(
                32,
                true,
                "MiniGame.Badminton.BadmintonMiniGameView",
                "curState",
                "Win");

            // 28 没有 View，OpenMiniGame 内直接 EndGame(28, true)。
            Templates[28] = new MiniGameTemplateInfo(28, true);
        }

        internal static IEnumerable<MiniGameTemplateInfo> All => Templates.Values;
        internal static IReadOnlyList<MiniGameTemplateInfo> ConcreteStageViews => StageViews;

        internal static bool HasDispatcher(int id) => Templates.ContainsKey(id);

        internal static bool TryGet(int id, out MiniGameTemplateInfo template) =>
            Templates.TryGetValue(id, out template);

        internal static bool TryGetByView(Type viewType, out MiniGameTemplateInfo template)
        {
            template = null;
            if (viewType == null)
            {
                return false;
            }

            foreach (MiniGameTemplateInfo candidate in StageViews)
            {
                Type expected = candidate.ResolveViewType();
                if (expected != null && expected.IsAssignableFrom(viewType))
                {
                    template = candidate;
                    return true;
                }
            }

            return false;
        }

        private static MiniGameTemplateInfo CreateFromAdapter(int id)
        {
            if (OriginalMinigameAdapterRegistry.TryGet(id, out IOriginalMinigameAdapter adapter))
            {
                return new MiniGameTemplateInfo(id, true, adapter.ViewTypeName);
            }

            return new MiniGameTemplateInfo(id, false);
        }

        private static MiniGameTemplateInfo RegisterStageView(
            int id,
            string viewTypeName,
            string outcomeField,
            string winEnumName = null,
            string selectIdField = null)
        {
            MiniGameTemplateInfo info = new MiniGameTemplateInfo(
                id,
                true,
                viewTypeName,
                outcomeField,
                winEnumName,
                selectIdField);
            Templates[id] = info;
            return info;
        }
    }
}
