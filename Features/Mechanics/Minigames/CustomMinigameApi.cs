using System;
using System.Collections.Generic;

namespace EC2BUnofficialPatch.Features.Mechanics.Minigames
{
    /// <summary>外部小游戏 DLL 必须实现的公开入口。</summary>
    public interface ICustomMinigame
    {
        void Open(CustomMinigameContext context);
    }

    /// <summary>由非官方补丁创建并传给外部小游戏。外部 DLL 只需在结束时调用 Complete。</summary>
    public sealed class CustomMinigameContext
    {
        private readonly Action<bool, int> _complete;
        private bool _completed;

        internal CustomMinigameContext(
            int gameId,
            int npcId,
            int actionCfgId,
            string sourceFile,
            IReadOnlyDictionary<string, string> parameters,
            IReadOnlyList<double> launchParameters,
            MiniGameFromType launchFrom,
            int launchSourceId,
            Action<bool, int> complete)
        {
            GameId = gameId;
            NpcId = npcId;
            ActionCfgId = actionCfgId;
            SourceFile = sourceFile ?? string.Empty;
            Parameters = parameters ?? new Dictionary<string, string>();
            LaunchParameters = launchParameters == null
                ? new List<double>()
                : new List<double>(launchParameters);
            LaunchFrom = launchFrom;
            LaunchSourceId = launchSourceId;
            _complete = complete ?? throw new ArgumentNullException(nameof(complete));
        }

        public int GameId { get; }
        public int NpcId { get; }
        public int ActionCfgId { get; }
        public string SourceFile { get; }

        /// <summary>CustomMinigamecfg.json 中 parameters 的静态配置。</summary>
        public IReadOnlyDictionary<string, string> Parameters { get; }

        /// <summary>本次 Talk/Option miniGame[1...] 的动态参数；后备 Level 启动通常为空。</summary>
        public IReadOnlyList<double> LaunchParameters { get; }

        /// <summary>本次启动来源，例如 Talk、Option 或 Level。</summary>
        public MiniGameFromType LaunchFrom { get; }

        /// <summary>TalkCfg/OptionCfg/MinigameActionCfg 的来源 ID。</summary>
        public int LaunchSourceId { get; }

        public bool IsCompleted => _completed;

        public void Complete(bool isWin, int selectId = 0)
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
            _complete(isWin, selectId);
        }
    }
}
