using System.Collections.Generic;
using LFBetterAudio.Audio;
using LFBetterAudio.Effects;
using LFBetterAudio.Timeline;
using Sdk;

namespace LFBetterAudio.Runtime
{
    internal enum TalkAdvanceOrigin
    {
        None,
        Manual,
        Automatic,
        FastForward,
        System
    }

    public sealed class BetterAudioSession
    {
        public long Token { get; set; }
        internal BetterAudioEffectRequest Request { get; set; }
        public int MusicId { get; set; }
        internal BetterAudioPlaybackScope Scope { get; set; }
        internal BetterAudioContentKind ContentKind { get; set; }
        public int PlayMode { get; set; }
        public int LyricSizeMode { get; set; }
        public int LyricColorMode { get; set; }
        public FloatingLyricsRuntimeState LyricsUiState { get; set; }
        public bool ShowLyrics { get; set; }
        public bool ShouldLoop { get; set; }
        // 仅 1163,10 使用：0=无限循环，正整数=总播放次数。
        public bool UsesRepeatCount { get; set; }
        public int RepeatCount { get; set; } = 1;
        public int CompletedPlayCount { get; set; }
        public int TalkId { get; set; }
        public TalkChannel Channel { get; set; }
        public BaseView OwnerView { get; set; }
        public bool RequiresOwner { get; set; }
        public float MinimumAdvanceAt { get; set; }
        internal TalkAdvanceOrigin PendingAdvanceOrigin { get; set; }
        public ResolvedAudio ResolvedAudio { get; set; }
        public List<LrcLine> Lyrics { get; set; } = new List<LrcLine>();
        internal Dictionary<int, SingingRoleInfo> SingingRoles { get; set; } =
            new Dictionary<int, SingingRoleInfo>();
        public int LyricIndex { get; set; } = -1;
        public float LastPlaybackTime { get; set; } = -1f;
        public float PausedAtSeconds { get; set; }
        public float PlaybackStartSeconds { get; set; }
        public float PlaybackEndSeconds { get; set; }
        public bool HasPlaybackEndBoundary { get; set; }
        public bool UsesManualSegmentLoop { get; set; }
        public bool IsLoading { get; set; }
        public bool IsPlaying { get; set; }
        public bool IsPaused { get; set; }
        // 同一 Talk 的播放指令后紧跟暂停指令时，即使音频尚在异步加载，
        // 也会在加载完成后直接进入暂停状态。
        public bool PendingPauseAfterLoad { get; set; }
        public bool IsTransitionTail { get; set; }
        public bool IsCancelled { get; set; }

        internal bool IsSinging => ContentKind == BetterAudioContentKind.Singing;
        internal bool BlocksBackgroundAutomaticAdvance =>
            Scope == BetterAudioPlaybackScope.Background &&
            ContentKind == BetterAudioContentKind.Singing;
    }
}
