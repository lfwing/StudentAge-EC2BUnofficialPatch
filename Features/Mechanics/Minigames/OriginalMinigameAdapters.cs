using System;
using System.Collections.Generic;

namespace EC2BUnofficialPatch.Features.Mechanics.Minigames
{
    internal enum OriginalMinigameCompletionMode
    {
        EndGameOrCallback,
        Callback,
        ResultPositive,
        CloseAsSuccess,
        ImmediateEndGame
    }

    internal interface IOriginalMinigameAdapter
    {
        int Id { get; }
        string ViewTypeName { get; }
        OriginalMinigameCompletionMode CompletionMode { get; }
        void BindCallbacks(ref Action success, ref Action fail, ref Action<float> result);
    }

    internal sealed class OriginalMinigameAdapter : IOriginalMinigameAdapter
    {
        internal OriginalMinigameAdapter(int id, string viewTypeName, OriginalMinigameCompletionMode completionMode)
        {
            Id = id;
            ViewTypeName = viewTypeName;
            CompletionMode = completionMode;
        }

        public int Id { get; }
        public string ViewTypeName { get; }
        public OriginalMinigameCompletionMode CompletionMode { get; }

        public void BindCallbacks(ref Action success, ref Action fail, ref Action<float> result)
        {
            MiniGameStageSession session = MiniGameStageCoordinator.Current;
            if (session == null || session.Settled)
            {
                return;
            }

            long token = session.Token;
            Action originalSuccess = success;
            Action originalFail = fail;
            Action<float> originalResult = result;

            success = delegate
            {
                MiniGameStageCoordinator.CompleteFromAdapter(token, true, 0, $"adapter-{Id}-success");
                originalSuccess?.Invoke();
            };
            fail = delegate
            {
                MiniGameStageCoordinator.CompleteFromAdapter(token, false, 0, $"adapter-{Id}-fail");
                originalFail?.Invoke();
            };

            if (CompletionMode == OriginalMinigameCompletionMode.ResultPositive)
            {
                result = value =>
                {
                    MiniGameStageCoordinator.CompleteFromAdapter(
                        token,
                        value > 0f,
                        Convert.ToInt32(value),
                        $"adapter-{Id}-result");
                    originalResult?.Invoke(value);
                };
            }
        }
    }

    internal static class OriginalMinigameAdapterRegistry
    {
        private static readonly Dictionary<int, IOriginalMinigameAdapter> Adapters =
            new Dictionary<int, IOriginalMinigameAdapter>
            {
                { 1,  A(1,  "MiniGame.Exam.Exam2MiniGameView", OriginalMinigameCompletionMode.CloseAsSuccess) },
                { 2,  A(2,  "MiniGame.Exam.ExamMiniGameView", OriginalMinigameCompletionMode.CloseAsSuccess) },
                { 3,  A(3,  "MiniGame.Divination.DivinationMiniGameView", OriginalMinigameCompletionMode.EndGameOrCallback) },
                { 4,  A(4,  "MiniGame.Hongbao.HongbaoMiniGameView", OriginalMinigameCompletionMode.CloseAsSuccess) },
                { 5,  A(5,  "MiniGame.Sudoku.SudokuMiniGameView", OriginalMinigameCompletionMode.EndGameOrCallback) },
                { 6,  A(6,  "MiniGame.Negotiation.NegotiationMiniGameView", OriginalMinigameCompletionMode.Callback) },
                { 7,  A(7,  "MiniGame.QuickCalc.QuickCalcMiniGameView", OriginalMinigameCompletionMode.Callback) },
                { 8,  A(8,  "MiniGame.Crossword.CrosswordMiniGameView", OriginalMinigameCompletionMode.Callback) },
                { 9,  A(9,  "MiniGame.Puzzle.PuzzleMiniGameView", OriginalMinigameCompletionMode.EndGameOrCallback) },
                { 10, A(10, "MiniGame.Other.BrickGameView", OriginalMinigameCompletionMode.EndGameOrCallback) },
                { 11, A(11, "MiniGame.Basketball.Basketball1On1View", OriginalMinigameCompletionMode.Callback) },
                { 13, A(13, "MiniGame.Sentence.SentenceMiniGameView", OriginalMinigameCompletionMode.Callback) },
                { 14, A(14, "MiniGame.CardMatch.CardMatchMiniGameView", OriginalMinigameCompletionMode.Callback) },
                { 15, A(15, "MiniGame.CardMatch.CardMatch2MiniGameView", OriginalMinigameCompletionMode.ResultPositive) },
                { 16, A(16, "MiniGame.Qte.Qte2MiniGameView", OriginalMinigameCompletionMode.Callback) },
                { 17, A(17, "MiniGame.Quiz.QuizMiniGameView", OriginalMinigameCompletionMode.Callback) },
                { 18, A(18, "MiniGame.Negotiation.NegotiationMatchMiniGameView", OriginalMinigameCompletionMode.ResultPositive) },
                { 19, A(19, "MiniGame.Hurdling.HurdlingMiniGameView", OriginalMinigameCompletionMode.Callback) },
                { 20, A(20, "MiniGame.FingerKnife.FingerKnifeMiniGameView", OriginalMinigameCompletionMode.EndGameOrCallback) },
                { 21, A(21, "MiniGame.Fight.FightMiniGameView", OriginalMinigameCompletionMode.Callback) },
                { 22, A(22, "MiniGame.Handicraft.HandicraftView", OriginalMinigameCompletionMode.Callback) },
                { 23, A(23, "MiniGame.MagicCube.MagicCubeMiniGameView", OriginalMinigameCompletionMode.Callback) },
                { 24, A(24, "MiniGame.Piano.PianoMiniGameView", OriginalMinigameCompletionMode.EndGameOrCallback) },
                { 26, A(26, "MiniGame.Badminton.BadmintonMiniGameView", OriginalMinigameCompletionMode.EndGameOrCallback) },
                { 27, A(27, "MiniGame.Running.RunningPartyView", OriginalMinigameCompletionMode.Callback) },
                { 28, A(28, null, OriginalMinigameCompletionMode.ImmediateEndGame) },
                { 29, A(29, "View.Love.PhotoboothView", OriginalMinigameCompletionMode.Callback) },
                { 30, A(30, "View.Love.PaintView", OriginalMinigameCompletionMode.Callback) },
                { 31, A(31, "View.Love.RibbonView", OriginalMinigameCompletionMode.Callback) },
                { 32, A(32, "MiniGame.Badminton.BadmintonMiniGameView", OriginalMinigameCompletionMode.EndGameOrCallback) },
                { 33, A(33, "MiniGame.StudyCard.StudyCardMiniGameView", OriginalMinigameCompletionMode.Callback) },
                { 34, A(34, "MiniGame.Qte.Qte3MiniGameView", OriginalMinigameCompletionMode.Callback) },
                { 35, A(35, "MiniGame.Lizong.LizongMiniGameView", OriginalMinigameCompletionMode.Callback) },
                { 36, A(36, "MiniGame.Negotiation.NegotiationTeamView", OriginalMinigameCompletionMode.ResultPositive) },
                { 37, A(37, "View.Love.LoveBreakfastView", OriginalMinigameCompletionMode.ResultPositive) },
                { 39, A(39, "MiniGame.TalkInput.TalkInputMinigameView", OriginalMinigameCompletionMode.ResultPositive) },
                { 41, A(41, "MiniGame.Music.MusicMiniGameView", OriginalMinigameCompletionMode.EndGameOrCallback) },
                { 42, A(42, "View.Skill.AnimeConView", OriginalMinigameCompletionMode.ResultPositive) },
                { 43, A(43, "MiniGame.Fishing.FishingMiniGameView", OriginalMinigameCompletionMode.EndGameOrCallback) },
                { 44, A(44, "View.Main.BirthdayPartyView", OriginalMinigameCompletionMode.ResultPositive) },
                { 45, A(45, "MiniGame.LineMatch.LineMatchMiniGameView", OriginalMinigameCompletionMode.Callback) },
                { 46, A(46, "MiniGame.Weaving.WeavingMinigameView", OriginalMinigameCompletionMode.EndGameOrCallback) },
                { 47, A(47, "View.Evt.ExpoPartyView", OriginalMinigameCompletionMode.ResultPositive) },
                { 48, A(48, "MiniGame.Drawing.DrawingMinigameView", OriginalMinigameCompletionMode.EndGameOrCallback) }
            };

        internal static IEnumerable<IOriginalMinigameAdapter> All => Adapters.Values;
        internal static bool TryGet(int id, out IOriginalMinigameAdapter adapter) => Adapters.TryGetValue(id, out adapter);

        private static IOriginalMinigameAdapter A(int id, string view, OriginalMinigameCompletionMode mode) =>
            new OriginalMinigameAdapter(id, view, mode);
    }
}
