using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Config;
using HarmonyLib;
using Sdk;

namespace EC2BUnofficialPatch.Features.Mechanics.Minigames
{
    internal enum SocialMinigameCategory
    {
        NativeStage,
        EmbeddedCallback,
        EmbeddedRequired,
        SpecialCompleteOnClose
    }

    internal enum MinigameResultPolicy
    {
        None,
        PositiveIsWin,
        AnyResultIsSuccess
    }

    internal enum OriginalMinigameSettlementMode
    {
        /// <summary>Level 入口由原版 View/MiniGameData.EndGame 独占结算。</summary>
        OriginalEndGame,

        /// <summary>原版玩法通过 success/fail/result 回调报告结果。</summary>
        Callback,

        /// <summary>没有统一结果回调，只能在具体 View 关闭时按适配规则结算。</summary>
        CloseObserved
    }

    internal enum MinigameOutcomeRule
    {
        None,
        BoolField,
        EnumFieldEquals,
        IntFieldEquals,
        IntFieldGreaterThan,
        CompareIntFields,
        CollectionFirstEquals,
        IntAtLeastCollectionCount
    }

    internal sealed class OriginalMinigameDescriptor
    {
        internal OriginalMinigameDescriptor(
            int id,
            string viewTypeName,
            SocialMinigameCategory category,
            OriginalMinigameLaunchAdapter launchAdapter,
            OriginalMinigameSettlementMode settlementMode,
            MinigameOutcomeRule outcomeRule = MinigameOutcomeRule.None,
            string outcomeField = null,
            string expectedValue = null,
            string secondaryField = null,
            string selectIdField = null,
            MinigameResultPolicy resultPolicy = MinigameResultPolicy.None,
            bool immediateSuccess = false)
        {
            Id = id;
            ViewTypeName = viewTypeName;
            Category = category;
            LaunchAdapter = launchAdapter ?? throw new ArgumentNullException(nameof(launchAdapter));
            SettlementMode = settlementMode;
            OutcomeRule = outcomeRule;
            OutcomeField = outcomeField;
            ExpectedValue = expectedValue;
            SecondaryField = secondaryField;
            SelectIdField = selectIdField;
            ResultPolicy = resultPolicy;
            ImmediateSuccess = immediateSuccess;
        }

        internal int Id { get; }
        internal string ViewTypeName { get; }
        internal SocialMinigameCategory Category { get; }
        internal OriginalMinigameLaunchAdapter LaunchAdapter { get; }
        internal OriginalMinigameSettlementMode SettlementMode { get; }
        internal MinigameOutcomeRule OutcomeRule { get; }
        internal string OutcomeField { get; }
        internal string ExpectedValue { get; }
        internal string SecondaryField { get; }
        internal string SelectIdField { get; }
        internal MinigameResultPolicy ResultPolicy { get; }
        internal bool ImmediateSuccess { get; }

        internal bool CanOpenAsFallback => LaunchAdapter.SupportsLevelFallback;
        internal bool CanOpenEmbedded => !ImmediateSuccess;
        internal bool CompleteOnClose => SettlementMode == OriginalMinigameSettlementMode.CloseObserved;
        internal bool NeedsCloseObservation =>
            !string.IsNullOrWhiteSpace(ViewTypeName) &&
            (SettlementMode == OriginalMinigameSettlementMode.OriginalEndGame || CompleteOnClose);

        internal bool ShouldBindCallbacks(bool embedded) =>
            embedded && SettlementMode == OriginalMinigameSettlementMode.Callback;

        internal bool ShouldObserveClose(bool embedded) =>
            CompleteOnClose ||
            (embedded && SettlementMode == OriginalMinigameSettlementMode.OriginalEndGame);

        internal bool TryValidateLevel(MinigameActionCfg action, out string error) =>
            LaunchAdapter.TryValidateLevel(action, out error);

        internal void OpenLevel(MiniGameStageSession session) =>
            LaunchAdapter.OpenLevel(session);

        internal Type ResolveViewType() =>
            string.IsNullOrWhiteSpace(ViewTypeName)
                ? null
                : AccessTools.TypeByName(ViewTypeName);

        internal void BindCallbacks(
            long token,
            ref Action success,
            ref Action fail,
            ref Action<float> result)
        {
            Action originalSuccess = success;
            Action originalFail = fail;
            Action<float> originalResult = result;

            success = delegate
            {
                MiniGameStageCoordinator.CompleteFromAdapter(
                    token,
                    true,
                    0,
                    $"original-{Id}-success");
                originalSuccess?.Invoke();
            };

            fail = delegate
            {
                MiniGameStageCoordinator.CompleteFromAdapter(
                    token,
                    false,
                    0,
                    $"original-{Id}-fail");

                if (originalFail != null)
                {
                    originalFail();
                }
                else
                {
                    // 原版 JumpToMiniGame 只把 NewTalkView 的流程 callback 放入 success 槽。
                    // Talk 内嵌玩法失败且没有后续 Talk 时，也必须继续原阶段流程。
                    MiniGameStageSession active = MiniGameStageCoordinator.Current;
                    if (active != null &&
                        active.Token == token &&
                        active.LaunchFrom == MiniGameFromType.Talk)
                    {
                        originalSuccess?.Invoke();
                    }
                }
            };

            if (ResultPolicy == MinigameResultPolicy.None)
            {
                return;
            }

            result = value =>
            {
                bool isWin = ResultPolicy == MinigameResultPolicy.AnyResultIsSuccess || value > 0f;
                MiniGameStageCoordinator.CompleteFromAdapter(
                    token,
                    isWin,
                    Convert.ToInt32(value),
                    $"original-{Id}-result");
                originalResult?.Invoke(value);
            };
        }

        internal bool TryReadOutcome(object view, out bool isWin, out int selectId)
        {
            isWin = false;
            selectId = 0;
            if (view == null || OutcomeRule == MinigameOutcomeRule.None)
            {
                return false;
            }

            FieldInfo primary = AccessTools.Field(view.GetType(), OutcomeField);
            if (primary == null)
            {
                return false;
            }

            object value = primary.GetValue(view);
            switch (OutcomeRule)
            {
                case MinigameOutcomeRule.BoolField:
                    if (!(value is bool boolValue)) return false;
                    isWin = boolValue;
                    break;

                case MinigameOutcomeRule.EnumFieldEquals:
                    if (value == null) return false;
                    isWin = string.Equals(
                        value.ToString(),
                        ExpectedValue,
                        StringComparison.OrdinalIgnoreCase);
                    break;

                case MinigameOutcomeRule.IntFieldEquals:
                    if (!TryConvertInt(value, out int intValue) ||
                        !int.TryParse(ExpectedValue, out int expectedInt)) return false;
                    isWin = intValue == expectedInt;
                    break;

                case MinigameOutcomeRule.IntFieldGreaterThan:
                    if (!TryConvertInt(value, out int greaterValue) ||
                        !int.TryParse(ExpectedValue, out int threshold)) return false;
                    isWin = greaterValue > threshold;
                    break;

                case MinigameOutcomeRule.CompareIntFields:
                    FieldInfo secondary = AccessTools.Field(view.GetType(), SecondaryField);
                    if (secondary == null ||
                        !TryConvertInt(value, out int left) ||
                        !TryConvertInt(secondary.GetValue(view), out int right)) return false;
                    isWin = left >= right;
                    break;

                case MinigameOutcomeRule.CollectionFirstEquals:
                    if (!(value is IList list) || list.Count == 0 ||
                        !TryConvertInt(list[0], out int first) ||
                        !int.TryParse(ExpectedValue, out int expectedFirst)) return false;
                    isWin = first == expectedFirst;
                    break;

                case MinigameOutcomeRule.IntAtLeastCollectionCount:
                    FieldInfo collectionField = AccessTools.Field(view.GetType(), SecondaryField);
                    if (collectionField == null ||
                        !TryConvertInt(value, out int countValue) ||
                        !(collectionField.GetValue(view) is ICollection collection)) return false;
                    isWin = countValue >= collection.Count;
                    break;

                default:
                    return false;
            }

            if (!string.IsNullOrWhiteSpace(SelectIdField))
            {
                FieldInfo selectField = AccessTools.Field(view.GetType(), SelectIdField);
                if (selectField != null && TryConvertInt(selectField.GetValue(view), out int selected))
                {
                    selectId = selected;
                }
            }

            return true;
        }

        private static bool TryConvertInt(object value, out int result)
        {
            try
            {
                if (value == null)
                {
                    result = 0;
                    return false;
                }

                result = Convert.ToInt32(value);
                return true;
            }
            catch
            {
                result = 0;
                return false;
            }
        }
    }

    internal static class OriginalMinigameCatalog
    {
        private static readonly Dictionary<int, OriginalMinigameDescriptor> Items =
            new Dictionary<int, OriginalMinigameDescriptor>
            {
                // A：原版已经具备 Level/EndGame 社交阶段协议。
                { 3,  D(3,  "MiniGame.Divination.DivinationMiniGameView", SocialMinigameCategory.NativeStage, MinigameOutcomeRule.BoolField, "agree", select: "selectId") },
                { 5,  D(5,  "MiniGame.Sudoku.SudokuMiniGameView", SocialMinigameCategory.NativeStage, MinigameOutcomeRule.EnumFieldEquals, "curState", "Win") },
                { 9,  D(9,  "MiniGame.Puzzle.PuzzleMiniGameView", SocialMinigameCategory.NativeStage, MinigameOutcomeRule.EnumFieldEquals, "curState", "Win") },
                { 10, D(10, "MiniGame.Other.BrickGameView", SocialMinigameCategory.NativeStage, MinigameOutcomeRule.BoolField, "isWin") },
                { 20, D(20, "MiniGame.FingerKnife.FingerKnifeMiniGameView", SocialMinigameCategory.NativeStage, MinigameOutcomeRule.BoolField, "isWin") },
                { 24, D(24, "MiniGame.Piano.PianoMiniGameView", SocialMinigameCategory.NativeStage, MinigameOutcomeRule.BoolField, "isWin") },
                { 26, D(26, "MiniGame.Badminton.BadmintonMiniGameView", SocialMinigameCategory.NativeStage, MinigameOutcomeRule.EnumFieldEquals, "curState", "Win") },
                { 28, D(28, null, SocialMinigameCategory.NativeStage, immediate: true) },
                { 32, D(32, "MiniGame.Badminton.BadmintonMiniGameView", SocialMinigameCategory.NativeStage, MinigameOutcomeRule.EnumFieldEquals, "curState", "Win") },
                { 41, D(41, "MiniGame.Music.MusicMiniGameView", SocialMinigameCategory.NativeStage, MinigameOutcomeRule.EnumFieldEquals, "curState", "Win") },
                { 43, D(43, "MiniGame.Fishing.FishingMiniGameView", SocialMinigameCategory.NativeStage, MinigameOutcomeRule.EnumFieldEquals, "curState", "Win") },
                { 46, D(46, "MiniGame.Weaving.WeavingMinigameView", SocialMinigameCategory.NativeStage, MinigameOutcomeRule.EnumFieldEquals, "curState", "Win") },
                { 48, D(48, "MiniGame.Drawing.DrawingMinigameView", SocialMinigameCategory.NativeStage, MinigameOutcomeRule.EnumFieldEquals, "curState", "Win") },

                // B：由原版入口提供回调上下文；只允许在 startTalk 中内嵌启动并由回调结算。
                { 7,  D(7,  "MiniGame.QuickCalc.QuickCalcMiniGameView", SocialMinigameCategory.EmbeddedCallback, MinigameOutcomeRule.BoolField, "isWin") },
                { 8,  D(8,  "MiniGame.Crossword.CrosswordMiniGameView", SocialMinigameCategory.EmbeddedCallback, MinigameOutcomeRule.BoolField, "isWin") },
                { 13, D(13, "MiniGame.Sentence.SentenceMiniGameView", SocialMinigameCategory.EmbeddedCallback, MinigameOutcomeRule.EnumFieldEquals, "state", "Win") },
                { 14, D(14, "MiniGame.CardMatch.CardMatchMiniGameView", SocialMinigameCategory.EmbeddedCallback, MinigameOutcomeRule.BoolField, "isWin") },
                { 15, D(15, "MiniGame.CardMatch.CardMatch2MiniGameView", SocialMinigameCategory.EmbeddedCallback, MinigameOutcomeRule.IntAtLeastCollectionCount, "matchCnt", secondary: "cells", result: MinigameResultPolicy.PositiveIsWin) },
                { 16, D(16, "MiniGame.Qte.Qte2MiniGameView", SocialMinigameCategory.EmbeddedCallback, MinigameOutcomeRule.IntFieldGreaterThan, "successCnt", "0") },
                { 19, D(19, "MiniGame.Hurdling.HurdlingMiniGameView", SocialMinigameCategory.EmbeddedCallback, MinigameOutcomeRule.BoolField, "isWin") },
                { 22, D(22, "MiniGame.Handicraft.HandicraftView", SocialMinigameCategory.EmbeddedCallback, MinigameOutcomeRule.EnumFieldEquals, "curState", "Win") },
                { 23, D(23, "MiniGame.MagicCube.MagicCubeMiniGameView", SocialMinigameCategory.EmbeddedCallback, MinigameOutcomeRule.EnumFieldEquals, "curState", "Win") },
                { 33, D(33, "MiniGame.StudyCard.StudyCardMiniGameView", SocialMinigameCategory.EmbeddedCallback, MinigameOutcomeRule.BoolField, "isWin") },
                { 34, D(34, "MiniGame.Qte.Qte3MiniGameView", SocialMinigameCategory.EmbeddedCallback, MinigameOutcomeRule.BoolField, "isWin") },
                { 35, D(35, "MiniGame.Lizong.LizongMiniGameView", SocialMinigameCategory.EmbeddedCallback, MinigameOutcomeRule.BoolField, "isWin") },
                { 45, D(45, "MiniGame.LineMatch.LineMatchMiniGameView", SocialMinigameCategory.EmbeddedCallback, MinigameOutcomeRule.BoolField, "isWin", result: MinigameResultPolicy.PositiveIsWin) },

                // C：依赖 Option/Talk/Evt 上下文或额外对象参数，只允许由 startTalk 内嵌打开。
                { 6,  D(6,  "MiniGame.Negotiation.NegotiationMiniGameView", SocialMinigameCategory.EmbeddedRequired, MinigameOutcomeRule.IntFieldEquals, "isWin", "1") },
                { 11, D(11, "MiniGame.Basketball.Basketball1On1View", SocialMinigameCategory.EmbeddedRequired, MinigameOutcomeRule.CompareIntFields, "playerScore", secondary: "aiScore") },
                { 17, D(17, "MiniGame.Quiz.QuizMiniGameView", SocialMinigameCategory.EmbeddedRequired, MinigameOutcomeRule.BoolField, "win") },
                { 18, D(18, "MiniGame.Negotiation.NegotiationMatchMiniGameView", SocialMinigameCategory.EmbeddedRequired, MinigameOutcomeRule.CollectionFirstEquals, "winners", "0") },
                { 21, D(21, "MiniGame.Fight.FightMiniGameView", SocialMinigameCategory.EmbeddedRequired, MinigameOutcomeRule.EnumFieldEquals, "curState", "Win") },
                { 27, D(27, "MiniGame.Running.RunningPartyView", SocialMinigameCategory.EmbeddedRequired, MinigameOutcomeRule.BoolField, "win") },

                // D/E：情侣或特殊业务玩法。仅在显式用于社交阶段时，若无明确结果则正常关闭即成功。
                { 1,  D(1,  "MiniGame.Exam.Exam2MiniGameView", SocialMinigameCategory.SpecialCompleteOnClose) },
                { 2,  D(2,  "MiniGame.Exam.ExamMiniGameView", SocialMinigameCategory.SpecialCompleteOnClose) },
                { 4,  D(4,  "MiniGame.Hongbao.HongbaoMiniGameView", SocialMinigameCategory.SpecialCompleteOnClose) },
                { 29, D(29, "View.Love.PhotoboothView", SocialMinigameCategory.SpecialCompleteOnClose) },
                { 30, D(30, "View.Love.PaintView", SocialMinigameCategory.SpecialCompleteOnClose, result: MinigameResultPolicy.AnyResultIsSuccess) },
                { 31, D(31, "View.Love.RibbonView", SocialMinigameCategory.SpecialCompleteOnClose, result: MinigameResultPolicy.AnyResultIsSuccess) },
                { 36, D(36, "MiniGame.Negotiation.NegotiationTeamView", SocialMinigameCategory.SpecialCompleteOnClose) },
                { 37, D(37, "View.Love.LoveBreakfastView", SocialMinigameCategory.SpecialCompleteOnClose) },
                { 39, D(39, "MiniGame.TalkInput.TalkInputMinigameView", SocialMinigameCategory.SpecialCompleteOnClose) },
                { 42, D(42, "View.Skill.AnimeConView", SocialMinigameCategory.SpecialCompleteOnClose) },
                { 44, D(44, "View.Main.BirthdayPartyView", SocialMinigameCategory.SpecialCompleteOnClose) },
                { 47, D(47, "View.Evt.ExpoPartyView", SocialMinigameCategory.SpecialCompleteOnClose) }
            };

        internal static IEnumerable<OriginalMinigameDescriptor> All => Items.Values;
        internal static bool HasDispatcher(int id) => Items.ContainsKey(id);
        internal static bool TryGet(int id, out OriginalMinigameDescriptor descriptor) =>
            Items.TryGetValue(id, out descriptor);

        private static OriginalMinigameDescriptor D(
            int id,
            string view,
            SocialMinigameCategory category,
            MinigameOutcomeRule rule = MinigameOutcomeRule.None,
            string field = null,
            string expected = null,
            string secondary = null,
            string select = null,
            MinigameResultPolicy result = MinigameResultPolicy.None,
            bool immediate = false) =>
            new OriginalMinigameDescriptor(
                id,
                view,
                category,
                OriginalMinigameAdapterRegistry.Require(id),
                GetSettlementMode(category),
                rule,
                field,
                expected,
                secondary,
                select,
                result,
                immediate);

        private static OriginalMinigameSettlementMode GetSettlementMode(
            SocialMinigameCategory category)
        {
            switch (category)
            {
                case SocialMinigameCategory.NativeStage:
                    return OriginalMinigameSettlementMode.OriginalEndGame;
                case SocialMinigameCategory.SpecialCompleteOnClose:
                    return OriginalMinigameSettlementMode.CloseObserved;
                default:
                    return OriginalMinigameSettlementMode.Callback;
            }
        }
    }
}
