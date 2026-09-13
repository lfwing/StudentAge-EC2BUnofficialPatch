using System;
using System.Collections.Generic;
using System.Linq;
using Config;
using LFBetterAudio.Audio;
using LFBetterAudio.Effects;
using LFBetterAudio.Timeline;
using Sdk;
using UnityEngine;

namespace LFBetterAudio.Runtime
{
    public sealed partial class BetterAudioController
    {
        private readonly Dictionary<long, BetterAudioEffectSession> _soundEffectSessions =
            new Dictionary<long, BetterAudioEffectSession>();

        // “当前音效”定义为最近一次成功建立、且尚未结束的插件音效会话。
        private long _currentSoundEffectToken;

        // 音效只拦截自动/系统推进。多个音效与音乐同时存在时，必须等所有相关
        // 解锁条件都满足后，才能继续此前被拦截的自动推进。
        private BaseView _pendingAudioAdvanceOwner;
        private TalkAdvanceOrigin _pendingAudioAdvanceOrigin;
        private float _pendingAudioAdvanceMinimumAt;

        private void StartSoundEffectSession(
            BetterAudioEffectRequest request,
            BaseView owner,
            TalkChannel channel,
            int talkId,
            Dictionary<int, AudioCfg> previewAudioCfgMap)
        {
            bool singleTalk = request.Scope == BetterAudioPlaybackScope.SingleTalk;
            if (singleTalk && owner == null)
            {
                Plugin.LogEffectError(
                    $"1163,{request.SourceSubcommand} 当前没有可绑定的 Talk，单一talk音效未执行。");
                return;
            }

            var context = new AudioResolveContext
            {
                Channel = channel,
                PreviewAudioCfgMap = previewAudioCfgMap
            };

            if (!AudioResolver.TryResolve(
                    request.MusicId,
                    context,
                    out ResolvedAudio resolved,
                    out string error))
            {
                Plugin.LogEffectError(error);
                return;
            }

            if (!IsRequestAudioTypeCompatible(request, resolved, out string typeError))
            {
                Plugin.LogEffectError(typeError);
                return;
            }

            var timeline = new List<LrcLine>();
            bool needsTimelineRange = request.HasStartLine || request.HasEndLine;
            if (needsTimelineRange)
            {
                if (string.IsNullOrWhiteSpace(resolved.TimelinePath))
                {
                    Plugin.LogEffectError(
                        $"1163,{request.SourceSubcommand} 使用 u/v 必须配置有效 LRC 时间轴，指令未执行。");
                    return;
                }

                try
                {
                    timeline = LrcParser.ParseFile(resolved.TimelinePath);
                }
                catch (Exception ex)
                {
                    Plugin.LogEffectError(
                        $"音效 ID={request.MusicId} 的 LRC 时间轴解析失败：{ex.Message}");
                    return;
                }

                if (timeline.Count == 0)
                {
                    Plugin.LogEffectError(
                        $"1163,{request.SourceSubcommand} 使用 u/v 必须存在至少一条有效时间轴记录，指令未执行。");
                    return;
                }

                if (!ValidateRequestedLineRange(request, timeline, out string rangeError))
                {
                    Plugin.LogEffectError(rangeError);
                    return;
                }
            }

            long token = ++_tokenCounter;
            var session = new BetterAudioEffectSession
            {
                Token = token,
                Request = request,
                AudioId = request.MusicId,
                Scope = request.Scope,
                PlayMode = request.PlayMode,
                TalkId = talkId,
                Channel = channel,
                OwnerView = owner,
                RequiresOwner = singleTalk,
                ResolvedAudio = resolved,
                Timeline = timeline,
                IsLoading = true
            };

            _soundEffectSessions[token] = session;
            _currentSoundEffectToken = token;

            RequestAudioClip(
                resolved.AudioPath,
                clip => HandleLoadedSoundEffectClipSafely(token, clip));
        }

        private void HandleLoadedSoundEffectClipSafely(long token, AudioClip clip)
        {
            try
            {
                HandleLoadedSoundEffectClip(token, clip);
            }
            catch (Exception ex)
            {
                FailSoundEffectSession(token, $"音效异步回调失败：{ex.Message}");
            }
        }

        private void HandleLoadedSoundEffectClip(long token, AudioClip clip)
        {
            if (!_soundEffectSessions.TryGetValue(token, out BetterAudioEffectSession session) ||
                session == null || session.IsCancelled)
            {
                return;
            }

            if (session.RequiresOwner && !IsOwnerAlive(session.OwnerView))
            {
                RemoveSoundEffectSession(token, "加载完成前 Talk 已关闭", false, false);
                return;
            }

            if (clip == null)
            {
                FailSoundEffectSession(token, $"音效 ID={session.AudioId} 加载失败。");
                return;
            }

            AudioSource source = CreateSoundEffectAudioSource(session, clip);
            if (source == null)
            {
                FailSoundEffectSession(token, $"音效 ID={session.AudioId} 无法创建独立 AudioSource。");
                return;
            }

            if (!ConfigureSoundEffectPlaybackWindow(session, clip, out string rangeError))
            {
                FailSoundEffectSession(token, rangeError);
                return;
            }

            source.loop = session.ShouldLoop && !session.UsesManualSegmentLoop;
            source.time = session.PlaybackStartSeconds;
            session.IsLoading = false;
            if (session.PendingPauseAfterLoad)
            {
                session.PendingPauseAfterLoad = false;
                session.IsPaused = true;
                session.IsPlaying = false;
                LogSoundEffectPlaybackSuccess(session);
                Plugin.LogEffectSuccess(
                    $"音效已完成加载并直接进入暂停状态；音效={session.ResolvedAudio?.Name ?? "未知"}" +
                    $"（ID={session.AudioId}）。");
                return;
            }

            source.Play();
            session.HasStartedPlayback = true;
            session.IsPlaying = true;
            session.IsPaused = false;
            LogSoundEffectPlaybackSuccess(session);
        }

        private AudioSource CreateSoundEffectAudioSource(
            BetterAudioEffectSession session,
            AudioClip clip)
        {
            try
            {
                var audioObject = new GameObject($"lf-BetterAudioSoundEffect-{session.Token}");
                DontDestroyOnLoad(audioObject);
                AudioSource source = audioObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                source.clip = clip;
                source.volume = Mathf.Clamp01(session.ResolvedAudio?.Volume ?? 1f);
                // 分段循环需要手动在区间终点跳回起点，不能使用 AudioSource.loop。
                source.loop = false;
                source.time = 0f;
                TryBindGameSoundMixer(source);

                session.AudioObject = audioObject;
                session.AudioSource = source;
                return source;
            }
            catch
            {
                return null;
            }
        }

        private void ProcessSoundEffects()
        {
            if (_soundEffectSessions.Count == 0)
            {
                return;
            }

            var sessions = new List<BetterAudioEffectSession>(_soundEffectSessions.Values);
            foreach (BetterAudioEffectSession session in sessions)
            {
                if (session == null)
                {
                    continue;
                }

                if (session.IsCancelled)
                {
                    RemoveSoundEffectSession(session.Token, "会话已取消", false, false);
                    continue;
                }

                if (session.RequiresOwner && !IsOwnerAlive(session.OwnerView))
                {
                    RemoveSoundEffectSession(session.Token, "单Talk音效 Owner 已失效", false, true);
                    continue;
                }

                if (session.IsLoading || session.IsPaused || !session.IsPlaying)
                {
                    continue;
                }

                AudioSource source = session.AudioSource;
                if (source == null || source.clip == null)
                {
                    FailSoundEffectSession(session.Token, $"音效 ID={session.AudioId} 的 AudioSource/clip 已失效。");
                    continue;
                }

                if (session.HasPlaybackEndBoundary &&
                    source.time >= session.PlaybackEndSeconds - 0.005f)
                {
                    if (session.ShouldLoop)
                    {
                        try
                        {
                            source.time = session.PlaybackStartSeconds;
                            source.Play();
                        }
                        catch (Exception ex)
                        {
                            FailSoundEffectSession(session.Token, $"循环音效重新播放失败：{ex.Message}");
                        }
                    }
                    else
                    {
                        RemoveSoundEffectSession(session.Token, "音效播放区间完成", true, true);
                    }
                    continue;
                }

                if (source.isPlaying)
                {
                    continue;
                }

                if (session.ShouldLoop)
                {
                    try
                    {
                        source.time = session.PlaybackStartSeconds;
                        source.Play();
                    }
                    catch (Exception ex)
                    {
                        FailSoundEffectSession(session.Token, $"循环音效重新播放失败：{ex.Message}");
                    }
                    continue;
                }

                RemoveSoundEffectSession(session.Token, "单次音效自然完成", true, true);
            }
        }

        private void PauseCurrentSoundEffect()
        {
            BetterAudioEffectSession session = GetCurrentSoundEffectSession();
            if (session == null || session.IsCancelled || session.IsPaused)
            {
                Plugin.LogEffectError("1163,99,3 暂停失败：当前没有正在播放的插件音效。");
                return;
            }

            if (session.IsLoading)
            {
                session.PendingPauseAfterLoad = true;
                Plugin.LogEffectSuccess(
                    $"已登记加载完成后直接暂停音效；音效 ID={session.AudioId}。");
                return;
            }

            AudioSource source = session.AudioSource;
            if (!session.IsPlaying || source == null || source.clip == null)
            {
                Plugin.LogEffectError("1163,99,3 暂停失败：当前音效 AudioSource 或音频已失效。");
                return;
            }

            try
            {
                source.Pause();
                session.IsPlaying = false;
                session.IsPaused = true;
                Plugin.LogEffectSuccess(
                    $"已暂停插件音效；音效={session.ResolvedAudio?.Name ?? "未知"}" +
                    $"（ID={session.AudioId}），暂停点={source.time:F3}秒。");
            }
            catch (Exception ex)
            {
                Plugin.LogEffectError($"1163,99,3 暂停音效失败：{ex.Message}");
            }
        }

        private void ResumeCurrentSoundEffect()
        {
            BetterAudioEffectSession session = GetCurrentSoundEffectSession();
            if (session == null || session.IsCancelled)
            {
                Plugin.LogEffectError("1163,99,4 恢复失败：当前没有已暂停的插件音效。");
                return;
            }

            if (session.IsLoading)
            {
                if (session.PendingPauseAfterLoad)
                {
                    session.PendingPauseAfterLoad = false;
                    Plugin.LogEffectSuccess(
                        $"已取消加载完成后的音效暂停；音效 ID={session.AudioId} 将在加载后正常播放。");
                    return;
                }

                Plugin.LogEffectError("1163,99,4 恢复失败：当前音效仍在加载且未处于暂停状态。");
                return;
            }

            if (!session.IsPaused || session.AudioSource == null || session.AudioSource.clip == null)
            {
                Plugin.LogEffectError("1163,99,4 恢复失败：当前没有已暂停的插件音效。");
                return;
            }

            try
            {
                if (session.HasStartedPlayback)
                {
                    session.AudioSource.UnPause();
                }
                else
                {
                    session.AudioSource.Play();
                    session.HasStartedPlayback = true;
                }

                session.IsPaused = false;
                session.IsPlaying = true;
                Plugin.LogEffectSuccess(
                    $"已恢复插件音效；音效={session.ResolvedAudio?.Name ?? "未知"}" +
                    $"（ID={session.AudioId}），恢复点={session.AudioSource.time:F3}秒。");
            }
            catch (Exception ex)
            {
                Plugin.LogEffectError($"1163,99,4 恢复音效失败：{ex.Message}");
            }
        }

        private BetterAudioEffectSession GetCurrentSoundEffectSession()
        {
            if (_currentSoundEffectToken != 0 &&
                _soundEffectSessions.TryGetValue(
                    _currentSoundEffectToken,
                    out BetterAudioEffectSession current) &&
                current != null && !current.IsCancelled)
            {
                return current;
            }

            BetterAudioEffectSession latest = _soundEffectSessions.Values
                .Where(session => session != null && !session.IsCancelled)
                .OrderByDescending(session => session.Token)
                .FirstOrDefault();
            _currentSoundEffectToken = latest?.Token ?? 0;
            return latest;
        }

        private void FailSoundEffectSession(long token, string error)
        {
            Plugin.LogEffectError(error);
            RemoveSoundEffectSession(token, error, false, true);
        }

        private void RemoveSoundEffectSession(
            long token,
            string reason,
            bool logNaturalCompletion,
            bool tryContinueAdvance)
        {
            if (!_soundEffectSessions.TryGetValue(token, out BetterAudioEffectSession session))
            {
                return;
            }

            _soundEffectSessions.Remove(token);
            if (session != null)
            {
                session.IsCancelled = true;
                DestroySoundEffectAudioObjects(session);
                if (logNaturalCompletion)
                {
                    Plugin.LogEffectSuccess(
                        $"插件音效播放完成；音效={session.ResolvedAudio?.Name ?? "未知"}" +
                        $"（ID={session.AudioId}）。");
                }
            }

            if (_currentSoundEffectToken == token)
            {
                _currentSoundEffectToken = 0;
                GetCurrentSoundEffectSession();
            }

            if (tryContinueAdvance)
            {
                TryContinuePendingAudioAdvance();
            }
        }

        private static void DestroySoundEffectAudioObjects(BetterAudioEffectSession session)
        {
            if (session == null)
            {
                return;
            }

            try
            {
                if (session.AudioSource != null)
                {
                    session.AudioSource.Stop();
                    session.AudioSource.clip = null;
                }
            }
            catch
            {
            }

            try
            {
                if (session.AudioObject != null)
                {
                    UnityEngine.Object.Destroy(session.AudioObject);
                }
            }
            catch
            {
            }

            session.AudioSource = null;
            session.AudioObject = null;
            session.IsPlaying = false;
            session.HasStartedPlayback = false;
            session.IsPaused = false;
            session.IsLoading = false;
        }

        private void StopAllSoundEffects(string reason)
        {
            foreach (long token in _soundEffectSessions.Keys.ToList())
            {
                RemoveSoundEffectSession(token, reason, false, false);
            }

            _soundEffectSessions.Clear();
            _currentSoundEffectToken = 0;
        }

        private void BeforeSoundEffectTalkRefresh(BaseView owner, int newTalkId)
        {
            foreach (BetterAudioEffectSession session in _soundEffectSessions.Values.ToList())
            {
                if (session == null || session.IsCancelled ||
                    session.Scope != BetterAudioPlaybackScope.SingleTalk ||
                    !ReferenceEquals(session.OwnerView, owner) ||
                    session.TalkId == newTalkId)
                {
                    continue;
                }

                RemoveSoundEffectSession(session.Token, "进入新的 Talk", false, false);
            }

            ClearPendingAudioAdvance(owner);
        }

        private void AfterSoundEffectTalkRefresh(BaseView owner, int newTalkId)
        {
            foreach (BetterAudioEffectSession session in _soundEffectSessions.Values)
            {
                if (session == null || session.IsCancelled ||
                    session.Scope != BetterAudioPlaybackScope.Background)
                {
                    continue;
                }

                session.OwnerView = owner;
                session.TalkId = newTalkId;
            }
        }

        private void CleanupSoundEffectsForView(BaseView owner, string reason)
        {
            foreach (BetterAudioEffectSession session in _soundEffectSessions.Values.ToList())
            {
                if (session == null || session.IsCancelled ||
                    !ReferenceEquals(session.OwnerView, owner))
                {
                    continue;
                }

                if (session.Scope == BetterAudioPlaybackScope.SingleTalk)
                {
                    RemoveSoundEffectSession(session.Token, reason, false, false);
                }
                else
                {
                    session.OwnerView = null;
                }
            }

            ClearPendingAudioAdvance(owner);
        }

        private bool HandleSoundEffectAdvanceGate(
            BaseView owner,
            TalkAdvanceOrigin origin)
        {
            if (origin == TalkAdvanceOrigin.Manual || origin == TalkAdvanceOrigin.FastForward)
            {
                ClearPendingAudioAdvance(owner);
                return true;
            }

            if (!HasBlockingSoundEffects(owner))
            {
                return true;
            }

            QueuePendingAudioAdvance(owner, origin, Time.unscaledTime);
            return false;
        }

        private bool HasBlockingSoundEffects(BaseView owner)
        {
            foreach (BetterAudioEffectSession session in _soundEffectSessions.Values)
            {
                if (session == null || session.IsCancelled || !session.BlocksAutomaticAdvance)
                {
                    continue;
                }

                if (ReferenceEquals(session.OwnerView, owner))
                {
                    return true;
                }
            }

            return false;
        }

        private bool ShouldTrackSoundEffectAutomaticAdvance(BaseView owner)
        {
            return owner != null && HasBlockingSoundEffects(owner);
        }

        private void QueuePendingAudioAdvance(
            BaseView owner,
            TalkAdvanceOrigin origin,
            float minimumAdvanceAt)
        {
            if (owner == null ||
                origin == TalkAdvanceOrigin.None ||
                origin == TalkAdvanceOrigin.Manual ||
                origin == TalkAdvanceOrigin.FastForward)
            {
                return;
            }

            if (!ReferenceEquals(_pendingAudioAdvanceOwner, owner))
            {
                _pendingAudioAdvanceMinimumAt = 0f;
            }

            _pendingAudioAdvanceOwner = owner;
            _pendingAudioAdvanceOrigin = origin;
            _pendingAudioAdvanceMinimumAt = Mathf.Max(
                _pendingAudioAdvanceMinimumAt,
                minimumAdvanceAt);
        }

        private void ContinueAdvanceWhenAllAudioReady(
            BaseView owner,
            TalkAdvanceOrigin origin,
            float minimumAdvanceAt)
        {
            if (owner == null || origin == TalkAdvanceOrigin.None)
            {
                TryContinuePendingAudioAdvance();
                return;
            }

            QueuePendingAudioAdvance(owner, origin, minimumAdvanceAt);
            TryContinuePendingAudioAdvance();
        }

        private void TryContinuePendingAudioAdvance()
        {
            BaseView owner = _pendingAudioAdvanceOwner;
            TalkAdvanceOrigin origin = _pendingAudioAdvanceOrigin;
            float minimumAdvanceAt = _pendingAudioAdvanceMinimumAt;
            if (owner == null || origin == TalkAdvanceOrigin.None)
            {
                return;
            }

            if (!IsOwnerAlive(owner))
            {
                ClearPendingAudioAdvance(null);
                return;
            }

            if (HasBlockingSoundEffects(owner) ||
                HasMusicAdvanceBlocker(owner, origin))
            {
                return;
            }

            ClearPendingAudioAdvance(null);
            ContinueAdvance(owner, origin, minimumAdvanceAt);
        }

        private bool HasMusicAdvanceBlocker(
            BaseView owner,
            TalkAdvanceOrigin origin)
        {
            BetterAudioSession session = _session;
            if (session == null || session.IsCancelled)
            {
                return false;
            }

            if (session.BlocksBackgroundAutomaticAdvance &&
                ReferenceEquals(session.OwnerView, owner))
            {
                return origin == TalkAdvanceOrigin.Automatic;
            }

            if (!IsBlockingSingleTalkSession(session, owner))
            {
                return false;
            }

            if (Time.unscaledTime < session.MinimumAdvanceAt)
            {
                return true;
            }

            switch (session.PlayMode)
            {
                case 1:
                    return origin != TalkAdvanceOrigin.Manual;
                case 2:
                    return origin != TalkAdvanceOrigin.Manual &&
                           origin != TalkAdvanceOrigin.System;
                case 3:
                    return true;
                default:
                    return false;
            }
        }

        private void ClearPendingAudioAdvance(BaseView owner)
        {
            if (owner != null && !ReferenceEquals(_pendingAudioAdvanceOwner, owner))
            {
                return;
            }

            _pendingAudioAdvanceOwner = null;
            _pendingAudioAdvanceOrigin = TalkAdvanceOrigin.None;
            _pendingAudioAdvanceMinimumAt = 0f;
        }

        private static bool ConfigureSoundEffectPlaybackWindow(
            BetterAudioEffectSession session,
            AudioClip clip,
            out string error)
        {
            error = null;
            session.PlaybackStartSeconds = 0f;
            session.PlaybackEndSeconds = clip.length;
            session.HasPlaybackEndBoundary = false;
            session.UsesManualSegmentLoop = false;

            BetterAudioEffectRequest request = session.Request;
            if (request == null || (!request.HasStartLine && !request.HasEndLine))
            {
                return true;
            }

            if (request.HasStartLine && request.StartLine > 0)
            {
                LrcLine startLine = FindSoundEffectTimelineLine(session.Timeline, request.StartLine);
                if (startLine == null)
                {
                    error = $"[1163,{request.SourceSubcommand}] 找不到时间轴起点 u={request.StartLine}。";
                    return false;
                }
                session.PlaybackStartSeconds = startLine.TimeSeconds;
            }

            if (request.HasEndLine)
            {
                LrcLine selectedLine = FindSoundEffectTimelineLine(session.Timeline, request.EndLine);
                if (selectedLine == null)
                {
                    error = $"[1163,{request.SourceSubcommand}] 找不到时间轴终点 v={request.EndLine}。";
                    return false;
                }

                float selectedTime = selectedLine.TimeSeconds;
                if (selectedTime >= clip.length - 0.005f)
                {
                    error = $"[1163,{request.SourceSubcommand}] 时间轴终点 v={request.EndLine} 的时间 " +
                            $"{selectedTime:F3}s 超出音频长度 {clip.length:F3}s。";
                    return false;
                }

                float end = clip.length;
                foreach (LrcLine candidate in session.Timeline)
                {
                    if (candidate.LineNumber > request.EndLine &&
                        candidate.TimeSeconds > selectedTime + 0.001f &&
                        candidate.TimeSeconds < end)
                    {
                        end = candidate.TimeSeconds;
                    }
                }

                session.PlaybackEndSeconds = Mathf.Min(clip.length, end);
                session.HasPlaybackEndBoundary =
                    session.PlaybackEndSeconds < clip.length - 0.005f;
            }

            if (session.PlaybackStartSeconds < 0f ||
                session.PlaybackStartSeconds >= clip.length - 0.005f)
            {
                error = $"[1163,{request.SourceSubcommand}] 时间轴起点 " +
                        $"{session.PlaybackStartSeconds:F3}s 超出音频长度 {clip.length:F3}s。";
                return false;
            }

            if (session.PlaybackEndSeconds <= session.PlaybackStartSeconds + 0.005f)
            {
                error = $"[1163,{request.SourceSubcommand}] 音效播放区间为空：" +
                        $"start={session.PlaybackStartSeconds:F3}s，end={session.PlaybackEndSeconds:F3}s。";
                return false;
            }

            session.UsesManualSegmentLoop = session.ShouldLoop &&
                (session.PlaybackStartSeconds > 0.001f || session.HasPlaybackEndBoundary);
            return true;
        }

        private static LrcLine FindSoundEffectTimelineLine(
            IList<LrcLine> timeline,
            int lineNumber)
        {
            if (timeline == null || lineNumber <= 0)
            {
                return null;
            }

            foreach (LrcLine line in timeline)
            {
                if (line != null && line.LineNumber == lineNumber)
                {
                    return line;
                }
            }
            return null;
        }

        private static bool IsRequestAudioTypeCompatible(
            BetterAudioEffectRequest request,
            ResolvedAudio resolved,
            out string error)
        {
            error = null;
            if (request == null || resolved == null)
            {
                return false;
            }

            if (request.IsSoundEffect)
            {
                if (resolved.AudioType == BetterAudioType.SoundEffect)
                {
                    return true;
                }

                error = "1163,3/30相关指令仅适用于“音效”文件，而目前使用的是“音乐”文件，" +
                        $"效果不执行。音频={resolved.Name ?? "未知"}（ID={resolved.Id}）。";
                return false;
            }

            if (resolved.AudioType == BetterAudioType.Music)
            {
                return true;
            }

            error = "1163,1/10/2/20相关指令仅适用于“音乐”文件，而目前使用的是“音效”文件，" +
                    $"效果不执行。音频={resolved.Name ?? "未知"}（ID={resolved.Id}）。";
            return false;
        }

        private static void LogSoundEffectPlaybackSuccess(BetterAudioEffectSession session)
        {
            if (session == null)
            {
                return;
            }

            string scopeName = session.Scope == BetterAudioPlaybackScope.SingleTalk
                ? "针对单一talk的音效"
                : "针对背景的音效";
            string playModeName = session.PlayMode == 2
                ? "2（循环播放）"
                : "1（播放一次）";
            string rangeDetail = string.Empty;
            BetterAudioEffectRequest request = session.Request;
            if (request != null && request.HasStartLine)
            {
                rangeDetail = $"；u={request.StartLine}";
                if (request.HasEndLine)
                {
                    rangeDetail += $"，v={request.EndLine}";
                }
            }

            Plugin.LogEffectSuccess(
                $"类型={scopeName}；音效={session.ResolvedAudio?.Name ?? "未知音效"}" +
                $"（ID={session.AudioId}）；播放类型={playModeName}{rangeDetail}。");
        }

        private void TryBindGameSoundMixer(AudioSource source)
        {
            if (source == null)
            {
                return;
            }

            try
            {
                AudioMgr audioMgr = AudioMgr.Ins;
                Channel channel = audioMgr?.GetChannel(0);
                AudioSource gameSource = channel?.source;
                if (gameSource != null)
                {
                    source.outputAudioMixerGroup = gameSource.outputAudioMixerGroup;
                }
            }
            catch
            {
                // Mixer 绑定失败不影响独立音效 AudioSource 播放。
            }
        }
    }
}
