using LFBetterAudio.Audio;
using LFBetterAudio.Effects;
using LFBetterAudio.Timeline;
using System.Collections.Generic;
using Sdk;
using UnityEngine;

namespace LFBetterAudio.Runtime
{
    /// <summary>
    /// 插件音效使用独立 AudioSource；多个音效会话可以同时存在。
    /// 音效不会持有原版 BGM 的优先级租约。
    /// </summary>
    internal sealed class BetterAudioEffectSession
    {
        internal long Token { get; set; }
        internal BetterAudioEffectRequest Request { get; set; }
        internal int AudioId { get; set; }
        internal BetterAudioPlaybackScope Scope { get; set; }
        internal int PlayMode { get; set; }
        internal int TalkId { get; set; }
        internal TalkChannel Channel { get; set; }
        internal BaseView OwnerView { get; set; }
        internal bool RequiresOwner { get; set; }
        internal ResolvedAudio ResolvedAudio { get; set; }
        // 音效不显示歌词；仅在使用 u/v 时，把 LRC 作为时间轴读取。
        internal List<LrcLine> Timeline { get; set; } = new List<LrcLine>();
        internal float PlaybackStartSeconds { get; set; }
        internal float PlaybackEndSeconds { get; set; }
        internal bool HasPlaybackEndBoundary { get; set; }
        internal bool UsesManualSegmentLoop { get; set; }
        internal GameObject AudioObject { get; set; }
        internal AudioSource AudioSource { get; set; }
        internal bool IsLoading { get; set; }
        internal bool IsPlaying { get; set; }
        internal bool HasStartedPlayback { get; set; }
        internal bool IsPaused { get; set; }
        internal bool PendingPauseAfterLoad { get; set; }
        internal bool IsCancelled { get; set; }

        internal bool ShouldLoop => PlayMode == 2;
        internal bool BlocksAutomaticAdvance => !IsCancelled;
    }
}
