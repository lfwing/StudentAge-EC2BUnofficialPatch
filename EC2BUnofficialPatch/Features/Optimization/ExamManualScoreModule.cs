using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Config;
using EC2BUnofficialPatch.Core;
using HarmonyLib;
using MiniGame.Exam;
using MiniGame.TalkInput;
using Sdk;
using TheEntity;
using View.Evt;

namespace EC2BUnofficialPatch.Features.Optimization
{
    internal sealed class ExamManualScoreModule : IPluginModule
    {
        private static readonly IReadOnlyList<ModuleLogItem> Items = new[]
        {
            new ModuleLogItem("优化", "普通考试允许手动输入成绩")
        };

        public string Key => "optimization.exam-manual-score";
        public IReadOnlyList<ModuleLogItem> LogItems => Items;

        public void Load(Harmony harmony, PluginServices services)
        {
            MethodInfo openMiniGame = AccessTools.Method(
                typeof(FuncMgr),
                "OpenMiniGame",
                new[]
                {
                    typeof(int), typeof(MiniGameFromType), typeof(int), typeof(List<double>),
                    typeof(Action), typeof(Action), typeof(Action<float>), typeof(int)
                });
            MethodInfo directExam = AccessTools.Method(typeof(StudyData), "ExamMiniGame", new[] { typeof(int) });
            if (openMiniGame == null || directExam == null)
                throw new MissingMethodException("普通考试入口不存在");

            HarmonyMethod funcPrefix = new HarmonyMethod(typeof(ExamManualScorePatches), nameof(ExamManualScorePatches.OpenMiniGamePrefix))
            {
                priority = Priority.First
            };
            HarmonyMethod studyPrefix = new HarmonyMethod(typeof(ExamManualScorePatches), nameof(ExamManualScorePatches.DirectExamPrefix))
            {
                priority = Priority.First
            };
            harmony.Patch(openMiniGame, prefix: funcPrefix);
            harmony.Patch(directExam, prefix: studyPrefix);
        }
    }

    internal static class ExamManualScorePatches
    {
        internal static bool OpenMiniGamePrefix(int _gameId)
        {
            if (_gameId != 2)
                return true;

            // FuncMgr.OpenMiniGame 的原版前置状态变化，拦截后必须补回。
            Game.TimeChange(1f);
            ShowChoice();
            return false;
        }

        internal static bool DirectExamPrefix()
        {
            ShowChoice();
            return false;
        }

        private static void ShowChoice()
        {
            Role main = Singleton<RoleMgr>.Ins.GetRole();
            if (main == null || Cfg.GradeCfgMap == null || !Cfg.GradeCfgMap.ContainsKey(main.Grade))
            {
                PatchLog.Error("优化模块-无法读取当前年级配置，普通考试回退为原版小游戏");
                OpenOriginalExam();
                return;
            }

            int maxScore = Math.Max(0, Cfg.GradeCfgMap[main.Grade].maxScore);
            HintHelper.ShowConfirm(
                $"本次普通考试可直接输入最终总分（0～{maxScore}），也可以继续进入原版小游戏。",
                () => OpenInput(main.Grade, maxScore),
                OpenOriginalExam,
                true,
                "普通考试",
                "手动输入",
                "进入小游戏",
                null,
                false);
        }

        private static void OpenInput(int grade, int maxScore)
        {
            UIMgr.OpenView<TalkInputCommonView>(
                UILayerType.None,
                null,
                new object[]
                {
                    $"请输入本次考试最终总分（0～{maxScore}）",
                    TalkInputContentType.Default,
                    // TalkInputCommonView 会先调用回调、再关闭自身。若在回调内立即打开
                    // 结算界面，随后 CloseView 会破坏 UIMgr 的顶层界面顺序。
                    new Action<string>(text => Singleton<TimerMgr>.Ins.FrameDelay(
                        () => Submit(text, grade, maxScore),
                        1,
                        1))
                });
        }

        private static void Submit(string text, int grade, int maxScore)
        {
            double value;
            string trimmed = text?.Trim();
            bool parsed = double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
                          double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            if (!parsed || double.IsNaN(value) || double.IsInfinity(value))
            {
                HintHelper.ShowHint(
                    "输入无效",
                    "请输入普通数字；空内容、NaN 和 Infinity 不会写入考试成绩。",
                    () => OpenInput(grade, maxScore),
                    false);
                return;
            }

            int score;
            if (value <= 0d)
                score = 0;
            else if (value >= maxScore)
                score = maxScore;
            else
                score = (int)Math.Round(value, MidpointRounding.AwayFromZero);

            score = Math.Max(0, Math.Min(maxScore, score));
            Singleton<RoleMgr>.Ins.GetStudyData(true).SaveExamResult(grade, score);
            HintHelper.ShowLoadingResult(
                DescCtrl.GetTxt(166),
                null,
                CompleteManualExam,
                DescCtrl.GetTxt(168),
                null,
                null,
                0,
                0,
                0,
                0,
                -1);
        }

        private static void CompleteManualExam()
        {
            // 完整复刻 ExamMiniGameView.CloseView 的收尾。普通考试通常由
            // EffectorSpecial 在 NewTalkView 内触发；若不关闭该事件界面，尽管统计界面
            // 已退出，底层事件层仍会继续拦截输入，表现为游戏无法操作。
            AudioMgrEx.PlayBgm(Singleton<RoundMgr>.Ins.GetRound() != 61);
            Control.SwitchActionMapToUI();
            if (UIMgr.IsViewOpened<NewTalkView>())
                UIMgr.CloseView<NewTalkView>();
        }

        private static void OpenOriginalExam()
        {
            UIMgr.OpenView<ExamMiniGameView>(UILayerType.None, null, Array.Empty<object>());
        }
    }
}
