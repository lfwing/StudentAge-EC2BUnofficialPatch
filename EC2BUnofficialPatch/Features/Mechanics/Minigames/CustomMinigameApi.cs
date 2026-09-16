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
        private bool _cancelled;
        private Func<bool> _begin;
        private Func<bool> _active;
        private Action _cancel;
        private bool _begun;
        private readonly int _thread = System.Threading.Thread.CurrentThread.ManagedThreadId;
        public event Action Invalidated;
        public bool IsActive => !_completed && !_cancelled && (_active?.Invoke() ?? true);
        public bool IsBegun => _begun;
        internal void BindLifecycle(Func<bool> begin, Func<bool> active, Action cancel)
        { _begin=begin; _active=active; _cancel=cancel; }
        internal void Invalidate()
        {
            _cancelled = true;
            var listeners = Invalidated; Invalidated = null;
            if (listeners == null) return;
            foreach (Action listener in listeners.GetInvocationList())
                try { listener(); } catch (Exception e) { Core.PatchLog.Warning("小游戏失效通知失败：" + e.Message); }
        }
        private void CheckThread()
        {
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != _thread)
                throw new InvalidOperationException("小游戏生命周期必须在 Unity 主线程调用");
        }
        /// <summary>准备页确认后调用。deferStart=true 时在此处扣费；余额不足返回 false。</summary>
        public bool Begin()
        {
            CheckThread();
            if (!IsActive) return false;
            if (_begun) return true;
            if (_begin != null && !_begin()) return false;
            _begun = true; return true;
        }
        /// <summary>取消本次会话，不算输赢、不发奖励；未开始时回滚消耗。</summary>
        public void Cancel()
        {
            CheckThread();
            if (!IsActive) return;
            _cancelled = true;
            _cancel?.Invoke();
        }

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

        /// <summary>逻辑小游戏 ID。社交阶段为 PersonGrowCfg.minigame；剧情启动为 miniGame[0]。</summary>
        public int GameId { get; }
        /// <summary>社交阶段的 NPC；普通剧情启动时为 0。</summary>
        public int NpcId { get; }
        /// <summary>当前 MinigameActionCfg 阶段 ID；普通剧情启动时为 0。</summary>
        public int ActionCfgId { get; }
        public string SourceFile { get; }

        /// <summary>CustomMinigamecfg.json 中 parameters 的静态配置。</summary>
        public IReadOnlyDictionary<string, string> Parameters { get; }

        /// <summary>本次 Talk/Option miniGame[1...] 的动态参数；后备 Level 启动通常为空。普通剧情启动时用它选择关卡。</summary>
        public IReadOnlyList<double> LaunchParameters { get; }

        /// <summary>本次启动来源，例如 Talk、Option 或 Level。</summary>
        public MiniGameFromType LaunchFrom { get; }

        /// <summary>TalkCfg/OptionCfg/MinigameActionCfg 的来源 ID。</summary>
        public int LaunchSourceId { get; }

        public bool IsCompleted => _completed;

        public void Complete(bool isWin, int selectId = 0)
        {
            CheckThread();
            if (!IsActive || !Begin())
            {
                return;
            }

            _completed = true;
            _complete(isWin, selectId);
        }
    }
}
