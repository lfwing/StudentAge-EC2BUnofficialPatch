using System;
using System.Collections.Generic;
using Config;
using GenUI.Talk;
using LFBetterAudio.Assets;
using LFBetterAudio.Audio;
using LFBetterAudio.Effects;
using LFBetterAudio.Timeline;
using LFBetterAudio.UI;
using Sdk;
using UnityEngine;
using View.Evt;

namespace LFBetterAudio.Runtime
{
    internal enum BetterAudioVolumeFadeKind
    {
        None,
        FadeOutPause,
        FadeInResume
    }

    public sealed partial class BetterAudioController : MonoBehaviour
    {
        public static BetterAudioController Instance { get; private set; }

        private const float SingleTalkMinimumHoldSeconds = 0.4f;
        private const float HoldToSkipSeconds = 1f;
        private const int HoldToSkipHotKey = 128;
        private const float PauseFadeDurationSeconds = 0.8f;

        private AudioSource _audioSource;
        private GameObject _audioObject;
        private BetterAudioSession _session;
        private FloatingLyricsOverlay _lyricsOverlay;
        private HoldToSkipOverlay _holdToSkipOverlay;
        private readonly FloatingLyricsPositionLockState _lyricsPositionLockState =
            new FloatingLyricsPositionLockState();
        private long _tokenCounter;

        private BetterAudioVolumeFadeKind _volumeFadeKind;
        private long _volumeFadeSessionToken;
        private float _volumeFadeStartedAt;
        private float _volumeFadeDuration;
        private float _volumeFadeFrom;
        private float _volumeFadeTo;

        // 预加载只缓存 ResMgr 已返回的 AudioClip，并复用同一路径的在途请求。
        // 不接管原版 AudioMgr，也不改变原版音频配置。
        private readonly Dictionary<string, AudioClip> _audioClipCache =
            new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<Action<AudioClip>>> _audioClipWaiters =
            new Dictionary<string, List<Action<AudioClip>>>(StringComparer.OrdinalIgnoreCase);

        // 插件音乐与原版 BGM 使用两个独立 AudioSource。
        // 仅当插件音乐实际播放时持有原版 BGM 的“优先级租约”；
        // 插件音乐暂停、停止、失败、自然结束或 Talk 中断时必须立即归还。
        private bool _gameMusicSuppressionHeld;
        private bool _resumeGameMusicWhenReleased;
        private bool _gameMusicWarningLogged;
        private AudioSource _suppressedGameMusicSource;

        private BaseView _advanceContextOwner;
        private TalkAdvanceOrigin _advanceContextOrigin;
        private int _advanceContextDepth;

        private BaseView _scheduledAdvanceOwner;
        private TalkAdvanceOrigin _scheduledAdvanceOrigin;
        private float _scheduledAdvanceAt;

        private BaseView _holdOwner;
        private float _holdProgress;
        private bool _applicationQuitting;

        public BetterAudioSession ActiveSession => _session;

        internal static BetterAudioController EnsureInstance()
        {
            if (IsControllerUsable(Instance))
            {
                return Instance;
            }

            try
            {
                Instance = null;
                var go = new GameObject("lf-BetterAudioController")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                DontDestroyOnLoad(go);
                return go.AddComponent<BetterAudioController>();
            }
            catch
            {
                return null;
            }
        }

        private void Awake()
        {
            if (IsControllerUsable(Instance) && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            _lyricsOverlay = new FloatingLyricsOverlay();
            _holdToSkipOverlay = new HoldToSkipOverlay();
        }

        private void Update()
        {
            // BaseUnityPlugin 组件可能在游戏启动阶段被销毁；
            // 持久控制器继续维持正常游戏与编辑器的关键 EFFECT Patch。
            Plugin.Instance?.TickPatchHealthFromPersistentController();
            ProcessScheduledAdvance();
            _lyricsOverlay?.Tick(Time.unscaledDeltaTime);
            ProcessVolumeFade();
            ProcessSoundEffects();

            BetterAudioSession session = _session;
            if (session == null || session.IsCancelled)
            {
                ReleaseGameMusicSuppression();
                return;
            }

            if (session.RequiresOwner && !IsOwnerAlive(session.OwnerView))
            {
                EndActiveSessionInternal("单Talk Owner 已失效", true, true, false);
                return;
            }

            if (!session.RequiresOwner && session.OwnerView != null && !IsOwnerAlive(session.OwnerView))
            {
                session.OwnerView = null;
                _lyricsOverlay?.Destroy();
            }

            // 加载中没有插件音乐实际发声；暂停/中断时也必须让原版 BGM 正常播放。
            if (session.IsLoading || session.IsPaused || !session.IsPlaying)
            {
                ReleaseGameMusicSuppression();
                return;
            }

            MaintainGameMusicSuppression();

            AudioSource source = GetUsableAudioSource();
            if (source == null || source.clip == null)
            {
                FailActiveSession("AudioSource/clip 在播放中失效");
                return;
            }

            try
            {
                if (HasReachedPlaybackEnd(session, source))
                {
                    HandlePlaybackEnded(session, source);
                    return;
                }

                if (session.ShowLyrics)
                {
                    UpdateLyrics(session, source);
                }

                if (!source.isPlaying)
                {
                    HandlePlaybackEnded(session, source);
                }
            }
            catch (Exception ex)
            {
                InvalidateAudioSource("Update访问失败");
                FailActiveSession($"播放控制器运行异常：{ex.Message}");
            }
        }

        internal void ExecuteRequest(
            BetterAudioEffectRequest request,
            BaseView owner,
            TalkChannel channel,
            int talkId,
            Dictionary<int, AudioCfg> previewAudioCfgMap = null,
            Dictionary<int, PersonCfg> previewPersonCfgMap = null,
            GenderDefine previewGender = GenderDefine.Unknown)
        {
            if (request == null)
            {
                return;
            }

            switch (request.Command)
            {
                case BetterAudioCommandKind.StopAndRefresh:
                    StopAndRefreshChannel("1163,0");
                    break;

                case BetterAudioCommandKind.Play:
                    if (request.IsSoundEffect)
                    {
                        StartSoundEffectSession(
                            request,
                            owner,
                            channel,
                            talkId,
                            previewAudioCfgMap);
                    }
                    else
                    {
                        StartSession(
                            request,
                            owner,
                            channel,
                            talkId,
                            previewAudioCfgMap,
                            previewPersonCfgMap,
                            previewGender);
                    }
                    break;

                case BetterAudioCommandKind.PauseOrResume:
                    switch (request.PauseAction)
                    {
                        case 1:
                            PauseActiveMusic(false);
                            break;
                        case 2:
                            ResumeActiveMusic(false);
                            break;
                        case 3:
                            PauseCurrentSoundEffect();
                            break;
                        case 4:
                            ResumeCurrentSoundEffect();
                            break;
                        case 10:
                            PauseActiveMusic(true);
                            break;
                        case 20:
                            ResumeActiveMusic(true);
                            break;
                    }
                    break;
            }
        }

        internal void PreloadRequest(
            BetterAudioEffectRequest request,
            TalkChannel channel,
            Dictionary<int, AudioCfg> previewAudioCfgMap = null)
        {
            if (request == null || request.Command != BetterAudioCommandKind.Play)
            {
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
                    out string _))
            {
                return;
            }

            if (!IsRequestAudioTypeCompatible(request, resolved, out string _))
            {
                return;
            }

            RequestAudioClip(resolved.AudioPath, null);
        }

        private void StartSession(
            BetterAudioEffectRequest request,
            BaseView owner,
            TalkChannel channel,
            int talkId,
            Dictionary<int, AudioCfg> previewAudioCfgMap,
            Dictionary<int, PersonCfg> previewPersonCfgMap,
            GenderDefine previewGender)
        {
            bool singleTalk = request.Scope == BetterAudioPlaybackScope.SingleTalk;
            if (singleTalk && owner == null)
            {
                Plugin.LogEffectError(
                    $"1163,{request.SourceSubcommand} 当前没有可绑定的 Talk，单一talk音乐未执行。");
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

            long token = _tokenCounter + 1;
            var session = new BetterAudioSession
            {
                Token = token,
                Request = request,
                MusicId = request.MusicId,
                Scope = request.Scope,
                ContentKind = request.ContentKind,
                PlayMode = request.PlayMode,
                LyricSizeMode = request.LyricSizeMode,
                LyricColorMode = request.LyricColorMode,
                LyricsUiState = new FloatingLyricsRuntimeState(
                    request.LyricSizeMode,
                    request.LyricColorMode,
                    request.ContentKind == BetterAudioContentKind.Singing,
                    _lyricsPositionLockState),
                ShowLyrics = request.ShowLyrics,
                ShouldLoop = request.ShouldLoop,
                UsesRepeatCount = request.UsesRepeatCount,
                RepeatCount = request.RepeatCount,
                CompletedPlayCount = 0,
                TalkId = talkId,
                Channel = channel,
                OwnerView = owner,
                RequiresOwner = singleTalk,
                MinimumAdvanceAt = singleTalk
                    ? Time.unscaledTime + SingleTalkMinimumHoldSeconds
                    : 0f,
                ResolvedAudio = resolved,
                IsLoading = true
            };

            bool needsLyricsForRange = request.HasStartLine || request.HasEndLine;
            bool shouldParseLyrics = session.ShowLyrics || needsLyricsForRange || session.IsSinging;
            if (shouldParseLyrics && !string.IsNullOrWhiteSpace(resolved.TimelinePath))
            {
                try
                {
                    session.Lyrics = LrcParser.ParseFile(resolved.TimelinePath);
                }
                catch (Exception ex)
                {
                    Plugin.LogEffectError(
                        $"音乐 ID={request.MusicId} 的 LRC 解析失败：{ex.Message}");
                    session.Lyrics = new List<LrcLine>();
                }
            }

            if (needsLyricsForRange && session.Lyrics.Count == 0)
            {
                Plugin.LogEffectError(
                    $"1163,{request.SourceSubcommand} 使用 u/v 必须存在至少一条有效时间歌词，指令未执行。");
                return;
            }

            if (!ValidateRequestedLineRange(request, session.Lyrics, out string rangeError))
            {
                Plugin.LogEffectError(rangeError);
                return;
            }

            if (session.IsSinging)
            {
                var roleIssues = new List<string>();
                session.SingingRoles = SingingRoleResolver.Resolve(
                    request.SingerRoleIds,
                    channel,
                    previewPersonCfgMap,
                    previewGender,
                    roleIssues);

                foreach (string roleIssue in roleIssues)
                {
                    Plugin.LogEffectError(roleIssue);
                }
            }

            // 到此说明参数、资源和 LRC 均通过；无效的新指令不会破坏旧会话。
            bool clipReady = TryGetCachedAudioClip(resolved.AudioPath, out AudioClip cachedClip);
            bool preserveSuppression = clipReady && _gameMusicSuppressionHeld;

            // 预加载命中时，旧插件音乐与新插件音乐在同一帧交接，并保留原版 BGM
            // 的暂停租约，避免原版 BGM 在换轨瞬间短暂恢复。未命中时仍按旧规则
            // 归还原版通道，不在加载期间制造静音占用。
            EndActiveSessionInternal(
                "新音乐替换旧会话",
                true,
                !preserveSuppression,
                false);
            token = ++_tokenCounter;
            session.Token = token;
            _session = session;

            if (singleTalk)
            {
                // 只在新单 Talk 会话建立时复位一次快进，不再逐帧接管全局 timeScale。
                DisableFastForwardNow(owner);
            }

            AttachLyricsIfPossible(session, owner);

            if (clipReady)
            {
                HandleLoadedClipSafely(token, cachedClip);
                return;
            }

            RequestAudioClip(
                resolved.AudioPath,
                clip => HandleLoadedClipSafely(token, clip));
        }

        private static bool ValidateRequestedLineRange(
            BetterAudioEffectRequest request,
            IList<LrcLine> lyrics,
            out string error)
        {
            error = null;
            if (!request.HasStartLine && !request.HasEndLine)
            {
                return true;
            }

            int lyricCount = lyrics?.Count ?? 0;
            if (request.StartLine > lyricCount)
            {
                error = $"[1163,{request.SourceSubcommand}] u={request.StartLine} 大于有效歌词总数 {lyricCount}，指令未执行。";
                return false;
            }

            if (request.HasEndLine && request.EndLine > lyricCount)
            {
                error = $"[1163,{request.SourceSubcommand}] v={request.EndLine} 大于有效歌词总数 {lyricCount}，指令未执行。";
                return false;
            }

            return true;
        }

        private bool TryGetCachedAudioClip(string audioPath, out AudioClip clip)
        {
            clip = null;
            if (string.IsNullOrWhiteSpace(audioPath) ||
                !_audioClipCache.TryGetValue(audioPath, out AudioClip cached))
            {
                return false;
            }

            if (cached == null)
            {
                _audioClipCache.Remove(audioPath);
                return false;
            }

            clip = cached;
            return true;
        }

        private void RequestAudioClip(string audioPath, Action<AudioClip> onLoaded)
        {
            if (string.IsNullOrWhiteSpace(audioPath))
            {
                onLoaded?.Invoke(null);
                return;
            }

            if (TryGetCachedAudioClip(audioPath, out AudioClip cached))
            {
                onLoaded?.Invoke(cached);
                return;
            }

            if (_audioClipWaiters.TryGetValue(
                    audioPath,
                    out List<Action<AudioClip>> existingWaiters))
            {
                if (onLoaded != null)
                {
                    existingWaiters.Add(onLoaded);
                }
                return;
            }

            var waiters = new List<Action<AudioClip>>();
            if (onLoaded != null)
            {
                waiters.Add(onLoaded);
            }
            _audioClipWaiters[audioPath] = waiters;

            try
            {
                ResMgr.LoadAudioAsync(audioPath, clip =>
                {
                    if (clip != null)
                    {
                        _audioClipCache[audioPath] = clip;
                    }

                    if (!_audioClipWaiters.TryGetValue(
                            audioPath,
                            out List<Action<AudioClip>> callbacks))
                    {
                        return;
                    }

                    _audioClipWaiters.Remove(audioPath);
                    foreach (Action<AudioClip> callback in callbacks)
                    {
                        try
                        {
                            callback?.Invoke(clip);
                        }
                        catch (Exception ex)
                        {
                            Plugin.LogEffectError($"音频加载回调异常：{ex.Message}");
                        }
                    }
                }, null, false);
            }
            catch (Exception ex)
            {
                _audioClipWaiters.Remove(audioPath);
                foreach (Action<AudioClip> callback in waiters)
                {
                    callback?.Invoke(null);
                }
                Plugin.LogEffectError($"音频预加载启动失败：{ex.Message}");
            }
        }

        private void HandleLoadedClipSafely(long token, AudioClip clip)
        {
            try
            {
                HandleLoadedClip(token, clip);
            }
            catch (Exception ex)
            {
                if (_session != null && _session.Token == token)
                {
                    FailActiveSession($"音频异步回调失败：{ex.Message}");
                }
            }
        }

        private void HandleLoadedClip(long token, AudioClip clip)
        {
            BetterAudioSession session = _session;
            if (session == null || session.Token != token || session.IsCancelled)
            {
                return;
            }

            if (session.RequiresOwner && !IsOwnerAlive(session.OwnerView))
            {
                EndActiveSessionInternal("加载完成前 Talk 已关闭", true, true, false);
                return;
            }

            if (clip == null)
            {
                FailActiveSession("音乐加载失败");
                return;
            }

            if (!ConfigurePlaybackWindow(session, clip, out string windowError))
            {
                FailActiveSession(windowError);
                return;
            }

            bool startImmediately = !session.PendingPauseAfterLoad;
            if (startImmediately)
            {
                // 到音频即将真正发声时才取得优先级；加载期间原版 BGM 保持正常。
                AcquireGameMusicSuppression();
            }

            if (!TryStartClipWithRecovery(session, clip, startImmediately, out string error))
            {
                FailActiveSession($"插件音乐播放失败：{error}");
                return;
            }

            session.IsLoading = false;
            ResetLyrics(session);

            if (session.PendingPauseAfterLoad)
            {
                session.PendingPauseAfterLoad = false;
                session.PausedAtSeconds = session.PlaybackStartSeconds;
                session.IsPlaying = false;
                session.IsPaused = true;
                ReleaseGameMusicSuppression();
                LogPlaybackSuccess(session);
                Plugin.LogEffectSuccess(
                    $"音乐已完成加载并直接进入暂停状态；音乐={session.ResolvedAudio?.Name ?? "未知"}" +
                    $"（ID={session.MusicId}），暂停点={session.PausedAtSeconds:F3}秒。");
                return;
            }

            session.IsPlaying = true;
            session.IsPaused = false;
            MaintainGameMusicSuppression();
            LogPlaybackSuccess(session);
        }

        private static void LogPlaybackSuccess(BetterAudioSession session)
        {
            if (session == null || session.Request == null)
            {
                return;
            }

            BetterAudioEffectRequest request = session.Request;
            string typeName = request.Scope == BetterAudioPlaybackScope.SingleTalk
                ? request.IsSinging
                    ? "针对单一talk的唱歌型音乐"
                    : "针对单一talk的背景型音乐"
                : request.IsSinging
                    ? "针对背景的唱歌型音乐"
                    : "针对背景的背景型音乐";

            string musicName = session.ResolvedAudio != null &&
                               !string.IsNullOrWhiteSpace(session.ResolvedAudio.Name)
                ? session.ResolvedAudio.Name
                : "未知音乐";

            string detail =
                $"类型={typeName}；音乐={musicName}（ID={session.MusicId}）；" +
                $"字号={FormatLyricSize(request.LyricSizeMode)}";

            if (request.IsSinging)
            {
                detail += $"；唱歌人={FormatSingingRoles(session)}";
            }
            else
            {
                detail += $"；歌词颜色={FormatLyricColor(request)}";
                if (request.UsesRepeatCount)
                {
                    detail += request.RepeatCount == 0
                        ? "；播放次数=无限循环"
                        : $"；播放次数={request.RepeatCount}次";
                }
            }

            if (request.HasStartLine)
            {
                detail += $"；u={request.StartLine}";
                if (request.HasEndLine)
                {
                    detail += $"，v={request.EndLine}";
                }
                else
                {
                    detail += "，播放至结束";
                }
            }

            Plugin.LogEffectSuccess(detail + "。");
        }

        private static string FormatLyricSize(int sizeMode)
        {
            switch (sizeMode)
            {
                case -1:
                    return "-1（不显示歌词）";
                case 2:
                    return "2（1.2倍）";
                case 3:
                    return "3（1.5倍）";
                case 4:
                    return "4（1.8倍）";
                default:
                    return "1（默认）";
            }
        }

        private static string FormatLyricColor(BetterAudioEffectRequest request)
        {
            if (request == null || !request.ShowLyrics)
            {
                return "不显示歌词";
            }

            int colorId = TimelineColorPalette.NormalizeAuthorColorId(request.LyricColorMode);
            return $"{colorId}（{TimelineColorPalette.GetName(colorId)}）";
        }

        private static string FormatSingingRoles(BetterAudioSession session)
        {
            BetterAudioEffectRequest request = session?.Request;
            if (request?.SingerRoleIds == null || request.SingerRoleIds.Count == 0)
            {
                return "未指定，按合唱处理";
            }

            var parts = new List<string>();
            for (int slot = 1; slot <= request.SingerRoleIds.Count; slot++)
            {
                int requestedRoleId = request.SingerRoleIds[slot - 1];
                SingingRoleInfo role = null;
                session.SingingRoles?.TryGetValue(slot, out role);

                string name = role?.Name ?? "合唱";
                string gender = FormatGender(role?.Gender ?? GenderDefine.Unknown);
                int resolvedRoleId = role != null ? role.RoleId : requestedRoleId;
                string roleIdText = resolvedRoleId < 0
                    ? $"原roleId={requestedRoleId}"
                    : $"roleId={resolvedRoleId}";
                parts.Add($"id{slot}={name}（{roleIdText}，{gender}）");
            }

            return string.Join("，", parts);
        }

        private static string FormatGender(GenderDefine gender)
        {
            if (gender == GenderDefine.Male)
            {
                return "男性";
            }
            if (gender == GenderDefine.Female)
            {
                return "女性";
            }
            return "合唱";
        }

        private static bool ConfigurePlaybackWindow(
            BetterAudioSession session,
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
                LrcLine startLine = FindLyricByLineNumber(session.Lyrics, request.StartLine);
                if (startLine == null)
                {
                    error = $"[1163,{request.SourceSubcommand}] 找不到起始歌词 u={request.StartLine}。";
                    return false;
                }
                session.PlaybackStartSeconds = startLine.TimeSeconds;
            }

            if (request.HasEndLine)
            {
                LrcLine selectedLine = FindLyricByLineNumber(session.Lyrics, request.EndLine);
                if (selectedLine == null)
                {
                    error = $"[1163,{request.SourceSubcommand}] 找不到终止歌词 v={request.EndLine}。";
                    return false;
                }

                float selectedLineTime = selectedLine.TimeSeconds;
                if (selectedLineTime >= clip.length - 0.005f)
                {
                    error = $"[1163,{request.SourceSubcommand}] 终止歌词 v={request.EndLine} 的时间 " +
                            $"{selectedLineTime:F3}s 超出音频长度 {clip.length:F3}s。";
                    return false;
                }

                // v 是包含式终点。句号按 LRC 文件从上到下生成；
                // 实际停止点取 v 之后、时间晚于 v 的第一条有效歌词。
                float end = clip.length;
                foreach (LrcLine candidate in session.Lyrics)
                {
                    if (candidate.LineNumber > request.EndLine &&
                        candidate.TimeSeconds > selectedLineTime + 0.001f &&
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
                error = $"[1163,{request.SourceSubcommand}] 起始歌词时间 {session.PlaybackStartSeconds:F3}s 超出音频长度 {clip.length:F3}s。";
                return false;
            }

            if (session.PlaybackEndSeconds <= session.PlaybackStartSeconds + 0.005f)
            {
                error = $"[1163,{request.SourceSubcommand}] 播放区间为空：" +
                        $"start={session.PlaybackStartSeconds:F3}s，end={session.PlaybackEndSeconds:F3}s。";
                return false;
            }

            session.UsesManualSegmentLoop = session.ShouldLoop &&
                (session.PlaybackStartSeconds > 0.001f || session.HasPlaybackEndBoundary);
            return true;
        }

        private static LrcLine FindLyricByLineNumber(
            IList<LrcLine> lyrics,
            int lineNumber)
        {
            if (lyrics == null || lineNumber <= 0)
            {
                return null;
            }

            for (int i = 0; i < lyrics.Count; i++)
            {
                if (lyrics[i] != null && lyrics[i].LineNumber == lineNumber)
                {
                    return lyrics[i];
                }
            }

            return null;
        }

        private bool TryStartClipWithRecovery(
            BetterAudioSession session,
            AudioClip clip,
            bool startImmediately,
            out string error)
        {
            error = null;
            Exception lastException = null;

            for (int attempt = 1; attempt <= 2; attempt++)
            {
                AudioSource source = EnsureAudioSource(
                    attempt == 1 ? "音频加载完成" : "首次启动失败后重建",
                    attempt == 2);

                if (source == null)
                {
                    continue;
                }

                TryBindGameMusicMixer(source);

                try
                {
                    source.Stop();
                    source.clip = clip;
                    source.volume = session.ResolvedAudio.Volume;
                    source.loop = session.ShouldLoop && !session.UsesManualSegmentLoop;
                    source.time = Mathf.Clamp(
                        session.PlaybackStartSeconds,
                        0f,
                        Mathf.Max(0f, clip.length - 0.01f));
                    if (startImmediately)
                    {
                        source.Play();
                    }
                    _audioSource = source;
                    return true;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    InvalidateAudioSource($"第{attempt}次启动失败");
                }
            }

            error = lastException == null
                ? "无法获得可用 AudioSource"
                : $"{lastException.GetType().Name}: {lastException.Message}";
            return false;
        }

        internal void BeginAdvance(BaseView owner, TalkAdvanceOrigin origin)
        {
            if (owner == null)
            {
                return;
            }

            if (_advanceContextDepth == 0)
            {
                _advanceContextOwner = owner;
                _advanceContextOrigin = origin;
            }

            if (ReferenceEquals(_advanceContextOwner, owner))
            {
                _advanceContextDepth++;
            }
        }

        internal void EndAdvance(BaseView owner)
        {
            if (_advanceContextDepth <= 0 || !ReferenceEquals(_advanceContextOwner, owner))
            {
                return;
            }

            _advanceContextDepth--;
            if (_advanceContextDepth == 0)
            {
                _advanceContextOwner = null;
                _advanceContextOrigin = TalkAdvanceOrigin.None;
            }
        }

        private TalkAdvanceOrigin GetAdvanceOrigin(BaseView owner)
        {
            if (_advanceContextDepth > 0 && ReferenceEquals(_advanceContextOwner, owner))
            {
                return _advanceContextOrigin;
            }

            // NewTalkView.DoTextEnd 在 Time.timeScale > 1 时会直接调用 OnClickNext。
            // 这条路径不会经过 SpeedUp 方法本身，因此必须在 NextTalk 入口再次识别。
            if (owner is NewTalkView && Time.timeScale > 1.001f)
            {
                return TalkAdvanceOrigin.FastForward;
            }

            return TalkAdvanceOrigin.System;
        }

        internal bool HandleNextTalkAttempt(BaseView owner)
        {
            BetterAudioSession session = _session;
            TalkAdvanceOrigin origin = GetAdvanceOrigin(owner);

            if (session != null && !session.IsCancelled &&
                session.BlocksBackgroundAutomaticAdvance)
            {
                session.OwnerView = owner;
                if (origin == TalkAdvanceOrigin.Automatic)
                {
                    session.PendingAdvanceOrigin = origin;
                    return false;
                }

                // 玩家手动推进或快进时不应遗留旧的自动推进请求。
                session.PendingAdvanceOrigin = TalkAdvanceOrigin.None;
                return HandleSoundEffectAdvanceGate(owner, origin);
            }

            if (!IsBlockingSingleTalkSession(session, owner))
            {
                return HandleSoundEffectAdvanceGate(owner, origin);
            }

            if (origin == TalkAdvanceOrigin.FastForward)
            {
                // 只在实际快进尝试到达最终推进入口时复位一次，不逐帧接管 timeScale。
                DisableFastForwardNow(owner);
            }

            if (Time.unscaledTime < session.MinimumAdvanceAt)
            {
                if (origin == TalkAdvanceOrigin.System && session.PlayMode == 2)
                {
                    ScheduleAdvance(owner, origin, session.MinimumAdvanceAt);
                }
                else if (origin != TalkAdvanceOrigin.Manual && session.PlayMode != 2)
                {
                    session.PendingAdvanceOrigin = origin;
                }
                return false;
            }

            switch (session.PlayMode)
            {
                case 1:
                    if (origin == TalkAdvanceOrigin.Manual)
                    {
                        BeginTransitionTail(session);
                        return HandleSoundEffectAdvanceGate(owner, origin);
                    }

                    session.PendingAdvanceOrigin = origin;
                    return false;

                case 2:
                    if (origin == TalkAdvanceOrigin.Manual || origin == TalkAdvanceOrigin.System)
                    {
                        BeginTransitionTail(session);
                        return HandleSoundEffectAdvanceGate(owner, origin);
                    }

                    return false;

                case 3:
                    if (origin != TalkAdvanceOrigin.Manual)
                    {
                        session.PendingAdvanceOrigin = origin;
                    }
                    return false;

                default:
                    return HandleSoundEffectAdvanceGate(owner, origin);
            }
        }

        private void BeginTransitionTail(BetterAudioSession session)
        {
            if (session == null || session.IsCancelled || session.IsTransitionTail)
            {
                return;
            }

            session.IsTransitionTail = true;
            session.PendingAdvanceOrigin = TalkAdvanceOrigin.None;
            ClearScheduledAdvance();
            ClearHoldState(session.OwnerView);
            _lyricsOverlay?.Destroy();
        }

        internal bool ShouldTrackAutomaticAdvance(BaseView owner)
        {
            BetterAudioSession session = _session;
            return IsBlockingSingleTalkSession(session, owner) ||
                   (session != null && !session.IsCancelled &&
                    session.BlocksBackgroundAutomaticAdvance &&
                    ReferenceEquals(session.OwnerView, owner)) ||
                   ShouldTrackSoundEffectAutomaticAdvance(owner);
        }

        internal bool ShouldBlockFastForward(BaseView owner)
        {
            return IsBlockingSingleTalkSession(_session, owner);
        }

        internal bool ShouldBlockClose(BaseView owner)
        {
            return IsBlockingSingleTalkSession(_session, owner) && _session.PlayMode == 3;
        }

        internal void BeforeTalkRefresh(
            BaseView owner,
            int newTalkId,
            bool deferSingleTalkEndUntilTextStart)
        {
            BeforeSoundEffectTalkRefresh(owner, newTalkId);
            BetterAudioSession session = _session;
            if (!IsOwnedSingleTalkSession(session, owner) || session.TalkId == newTalkId)
            {
                return;
            }

            session.PendingAdvanceOrigin = TalkAdvanceOrigin.None;
            ClearScheduledAdvance();
            ClearHoldState(owner);

            if (!deferSingleTalkEndUntilTextStart)
            {
                EndActiveSessionInternal("进入新的 Talk", true, true, false);
            }
        }

        internal void FinalizeTextStart(BaseView owner, int currentTalkId)
        {
            BetterAudioSession session = _session;
            if (IsOwnedSingleTalkSession(session, owner) && session.TalkId != currentTalkId)
            {
                EndActiveSessionInternal("新 Talk 开始且没有可用的新 1163 会话", true, true, false);
            }
        }

        internal void AfterTalkRefresh(BaseView owner, int newTalkId)
        {
            AfterSoundEffectTalkRefresh(owner, newTalkId);
            BetterAudioSession session = _session;
            if (session == null || session.IsCancelled ||
                session.Scope != BetterAudioPlaybackScope.Background)
            {
                return;
            }

            session.OwnerView = owner;
            session.TalkId = newTalkId;
            AttachLyricsIfPossible(session, owner);
        }

        internal void CleanupForView(BaseView owner, string reason)
        {
            CleanupSoundEffectsForView(owner, reason);
            BetterAudioSession session = _session;
            if (session == null)
            {
                ClearHoldState(owner);
                return;
            }

            if (IsOwnedSingleTalkSession(session, owner))
            {
                EndActiveSessionInternal(reason, true, true, false);
            }
            else if (session.Scope == BetterAudioPlaybackScope.Background &&
                     ReferenceEquals(session.OwnerView, owner))
            {
                session.OwnerView = null;
                _lyricsOverlay?.Destroy();
            }

            ClearHoldState(owner);
        }

        internal bool HandleHoldStart(BaseView owner, int hotKey)
        {
            if (hotKey != HoldToSkipHotKey || !IsLockedModeThree(owner))
            {
                return false;
            }

            if (!(owner is NewTalkUI talkUi))
            {
                return false;
            }

            _holdOwner = owner;
            _holdProgress = 0f;
            _holdToSkipOverlay?.Show(talkUi);
            return true;
        }

        internal bool HandleHoldTick(BaseView owner, int hotKey)
        {
            if (hotKey != HoldToSkipHotKey || !ReferenceEquals(_holdOwner, owner) || !IsLockedModeThree(owner))
            {
                return false;
            }

            _holdProgress = Mathf.Clamp01(
                _holdProgress + Time.unscaledDeltaTime / HoldToSkipSeconds);
            _holdToSkipOverlay?.SetProgress(_holdProgress);

            if (_holdProgress < 1f)
            {
                return true;
            }

            _holdToSkipOverlay?.Hide();
            _holdOwner = null;
            _holdProgress = 0f;

            EndActiveSessionInternal("长按鼠标跳过类型3音乐", true, true, false);
            InvokeNextTalk(owner);
            return true;
        }

        internal bool HandleHoldRelease(BaseView owner, int hotKey)
        {
            if (hotKey != HoldToSkipHotKey ||
                (!ReferenceEquals(_holdOwner, owner) && !IsLockedModeThree(owner)))
            {
                return false;
            }

            ClearHoldState(owner);
            return true;
        }

        private bool IsLockedModeThree(BaseView owner)
        {
            return IsBlockingSingleTalkSession(_session, owner) && _session.PlayMode == 3;
        }

        private void ClearHoldState(BaseView owner)
        {
            if (_holdOwner != null && owner != null && !ReferenceEquals(_holdOwner, owner))
            {
                return;
            }

            _holdOwner = null;
            _holdProgress = 0f;
            _holdToSkipOverlay?.Hide();
        }

        private void CompleteOneShot(BetterAudioSession session)
        {
            if (_session == null || !ReferenceEquals(_session, session) || session.IsCancelled)
            {
                return;
            }

            BaseView owner = session.OwnerView;
            TalkAdvanceOrigin pending = session.PendingAdvanceOrigin;
            float minimumAdvanceAt = session.MinimumAdvanceAt;

            EndActiveSessionInternal("单次播放自然完成", true, true, false);

            if (pending != TalkAdvanceOrigin.None && IsOwnerAlive(owner))
            {
                ContinueAdvanceWhenAllAudioReady(owner, pending, minimumAdvanceAt);
            }
            else
            {
                TryContinuePendingAudioAdvance();
            }
        }

        private void FailActiveSession(string reason)
        {
            BetterAudioSession session = _session;
            BaseView owner = session?.OwnerView;
            TalkAdvanceOrigin pending = session?.PendingAdvanceOrigin ?? TalkAdvanceOrigin.None;
            float minimumAdvanceAt = session?.MinimumAdvanceAt ?? 0f;

            Plugin.LogEffectError(reason);
            EndActiveSessionInternal(reason, true, true, false);

            if (pending != TalkAdvanceOrigin.None && IsOwnerAlive(owner))
            {
                ContinueAdvanceWhenAllAudioReady(owner, pending, minimumAdvanceAt);
            }
            else
            {
                TryContinuePendingAudioAdvance();
            }
        }

        private void ContinueAdvance(
            BaseView owner,
            TalkAdvanceOrigin origin,
            float minimumAdvanceAt)
        {
            if (Time.unscaledTime < minimumAdvanceAt)
            {
                ScheduleAdvance(owner, origin, minimumAdvanceAt);
                return;
            }

            InvokeNextTalk(owner);
        }

        private void ScheduleAdvance(
            BaseView owner,
            TalkAdvanceOrigin origin,
            float executeAt)
        {
            _scheduledAdvanceOwner = owner;
            _scheduledAdvanceOrigin = origin;
            _scheduledAdvanceAt = Mathf.Max(Time.unscaledTime, executeAt);
        }

        private void ProcessScheduledAdvance()
        {
            BaseView owner = _scheduledAdvanceOwner;
            if (owner == null)
            {
                return;
            }

            if (!IsOwnerAlive(owner))
            {
                ClearScheduledAdvance();
                return;
            }

            if (Time.unscaledTime < _scheduledAdvanceAt)
            {
                return;
            }

            TalkAdvanceOrigin origin = _scheduledAdvanceOrigin;
            ClearScheduledAdvance();

            BeginAdvance(owner, origin);
            try
            {
                InvokeNextTalk(owner);
            }
            finally
            {
                EndAdvance(owner);
            }
        }

        private void ClearScheduledAdvance()
        {
            _scheduledAdvanceOwner = null;
            _scheduledAdvanceOrigin = TalkAdvanceOrigin.None;
            _scheduledAdvanceAt = 0f;
        }

        private static void InvokeNextTalk(BaseView owner)
        {
            try
            {
                if (owner is NewTalkView runtimeView)
                {
                    runtimeView.NextTalk();
                }
                else if (owner is PreviewTalkView previewView)
                {
                    previewView.NextTalk();
                }
            }
            catch (Exception ex)
            {
                Plugin.LogEffectError($"音乐播放完成后进入 nexttalk 失败：{ex.Message}");
            }
        }

        private void PauseActiveMusic(bool useFade)
        {
            BetterAudioSession session = _session;
            string command = useFade ? "1163,99,10" : "1163,99,1";
            if (session == null || session.IsCancelled || session.IsPaused)
            {
                Plugin.LogEffectError($"{command} 暂停失败：当前没有正在播放的插件音乐。");
                return;
            }

            // 同一 Talk 中“播放 -> 暂停”按 EFFECT 顺序执行。若音频尚未加载，
            // 不再把暂停判为失败，而是登记为加载完成后直接暂停。
            if (session.IsLoading)
            {
                session.PendingPauseAfterLoad = true;
                Plugin.LogEffectSuccess(
                    $"已登记加载完成后直接暂停；音乐 ID={session.MusicId}。");
                return;
            }

            if (!session.IsPlaying)
            {
                Plugin.LogEffectError($"{command} 暂停失败：当前没有正在播放的插件音乐。");
                return;
            }

            AudioSource source = GetUsableAudioSource();
            if (source == null || source.clip == null)
            {
                Plugin.LogEffectError($"{command} 暂停失败：插件 AudioSource 或音频已失效。");
                return;
            }

            try
            {
                if (useFade)
                {
                    StartVolumeFade(
                        session,
                        source,
                        BetterAudioVolumeFadeKind.FadeOutPause,
                        source.volume,
                        0f,
                        PauseFadeDurationSeconds);
                    Plugin.LogEffectSuccess(
                        $"已开始淡出暂停；音乐={session.ResolvedAudio?.Name ?? "未知"}" +
                        $"（ID={session.MusicId}），淡出时长={PauseFadeDurationSeconds:F2}秒。");
                    return;
                }

                CancelVolumeFade(false);
                CompletePause(session, source, "已暂停插件音乐");
            }
            catch (Exception ex)
            {
                Plugin.LogEffectError($"{command} 暂停失败：{ex.Message}");
            }
        }

        private void ResumeActiveMusic(bool useFade)
        {
            BetterAudioSession session = _session;
            string command = useFade ? "1163,99,20" : "1163,99,2";
            if (session == null || session.IsCancelled)
            {
                Plugin.LogEffectError($"{command} 恢复失败：当前没有已暂停的插件音乐。");
                return;
            }

            // 加载尚未完成时，恢复指令可以撤销此前登记的 PendingPauseAfterLoad，
            // 从而保证同一 EFFECT 批次严格按出现顺序得到确定结果。
            if (session.IsLoading)
            {
                if (session.PendingPauseAfterLoad)
                {
                    session.PendingPauseAfterLoad = false;
                    Plugin.LogEffectSuccess(
                        $"已取消加载完成后的暂停；音乐 ID={session.MusicId} 将在加载后正常播放。");
                    return;
                }

                Plugin.LogEffectError($"{command} 恢复失败：音乐仍在加载且未处于暂停状态。");
                return;
            }

            if (!session.IsPaused)
            {
                Plugin.LogEffectError($"{command} 恢复失败：当前没有已暂停的插件音乐。");
                return;
            }

            AudioSource source = GetUsableAudioSource();
            if (source == null || source.clip == null)
            {
                Plugin.LogEffectError($"{command} 恢复失败：插件 AudioSource 或音频已失效。");
                return;
            }

            try
            {
                CancelVolumeFade(false);
                AcquireGameMusicSuppression();

                float maxTime = session.HasPlaybackEndBoundary
                    ? Mathf.Max(session.PlaybackStartSeconds, session.PlaybackEndSeconds - 0.01f)
                    : Mathf.Max(0f, source.clip.length - 0.01f);
                float resumeTime = session.PausedAtSeconds;
                if (resumeTime < session.PlaybackStartSeconds || resumeTime >= maxTime)
                {
                    resumeTime = session.PlaybackStartSeconds;
                }

                source.time = Mathf.Clamp(resumeTime, session.PlaybackStartSeconds, maxTime);
                source.loop = session.ShouldLoop && !session.UsesManualSegmentLoop;
                float targetVolume = GetSessionTargetVolume(session);
                source.volume = useFade ? 0f : targetVolume;
                source.Play();

                session.IsPaused = false;
                session.IsPlaying = true;
                ResetLyrics(session);
                MaintainGameMusicSuppression();

                if (useFade)
                {
                    StartVolumeFade(
                        session,
                        source,
                        BetterAudioVolumeFadeKind.FadeInResume,
                        0f,
                        targetVolume,
                        PauseFadeDurationSeconds);
                    Plugin.LogEffectSuccess(
                        $"已从暂停点淡入恢复；音乐={session.ResolvedAudio?.Name ?? "未知"}" +
                        $"（ID={session.MusicId}），恢复点={source.time:F3}秒，" +
                        $"淡入时长={PauseFadeDurationSeconds:F2}秒。");
                }
                else
                {
                    Plugin.LogEffectSuccess(
                        $"已恢复插件音乐；音乐={session.ResolvedAudio?.Name ?? "未知"}" +
                        $"（ID={session.MusicId}），恢复点={source.time:F3}秒。");
                }
            }
            catch (Exception ex)
            {
                CancelVolumeFade(false);
                ReleaseGameMusicSuppression();
                Plugin.LogEffectError($"{command} 恢复失败：{ex.Message}");
            }
        }

        private void StartVolumeFade(
            BetterAudioSession session,
            AudioSource source,
            BetterAudioVolumeFadeKind kind,
            float from,
            float to,
            float duration)
        {
            CancelVolumeFade(false);
            _volumeFadeKind = kind;
            _volumeFadeSessionToken = session.Token;
            _volumeFadeStartedAt = Time.unscaledTime;
            _volumeFadeDuration = Mathf.Max(0.01f, duration);
            _volumeFadeFrom = Mathf.Max(0f, from);
            _volumeFadeTo = Mathf.Max(0f, to);
            source.volume = _volumeFadeFrom;
        }

        private void ProcessVolumeFade()
        {
            if (_volumeFadeKind == BetterAudioVolumeFadeKind.None)
            {
                return;
            }

            BetterAudioSession session = _session;
            AudioSource source = GetUsableAudioSource();
            if (session == null || session.IsCancelled ||
                session.Token != _volumeFadeSessionToken ||
                source == null || source.clip == null)
            {
                ClearVolumeFadeState();
                return;
            }

            float progress = Mathf.Clamp01(
                (Time.unscaledTime - _volumeFadeStartedAt) / _volumeFadeDuration);
            float smoothed = Mathf.SmoothStep(0f, 1f, progress);
            source.volume = Mathf.Lerp(_volumeFadeFrom, _volumeFadeTo, smoothed);
            if (progress < 1f)
            {
                return;
            }

            BetterAudioVolumeFadeKind completedKind = _volumeFadeKind;
            ClearVolumeFadeState();
            if (completedKind == BetterAudioVolumeFadeKind.FadeOutPause)
            {
                CompletePause(session, source, "已淡出并暂停插件音乐");
            }
            else
            {
                source.volume = GetSessionTargetVolume(session);
            }
        }

        private void CompletePause(
            BetterAudioSession session,
            AudioSource source,
            string actionText)
        {
            if (session == null || source == null)
            {
                return;
            }

            session.PausedAtSeconds = source.time;
            source.Pause();
            // 保留目标音量，确保之后使用立即恢复时不会以 0 音量继续播放。
            source.volume = GetSessionTargetVolume(session);
            session.IsPaused = true;
            session.IsPlaying = false;
            _lyricsOverlay?.Clear();
            ReleaseGameMusicSuppression();
            Plugin.LogEffectSuccess(
                $"{actionText}；音乐={session.ResolvedAudio?.Name ?? "未知"}" +
                $"（ID={session.MusicId}），暂停点={session.PausedAtSeconds:F3}秒。");
        }

        private void CancelVolumeFade(bool restoreTargetVolume)
        {
            if (restoreTargetVolume && _session != null)
            {
                AudioSource source = GetUsableAudioSource();
                if (source != null)
                {
                    source.volume = GetSessionTargetVolume(_session);
                }
            }

            ClearVolumeFadeState();
        }

        private void ClearVolumeFadeState()
        {
            _volumeFadeKind = BetterAudioVolumeFadeKind.None;
            _volumeFadeSessionToken = 0;
            _volumeFadeStartedAt = 0f;
            _volumeFadeDuration = 0f;
            _volumeFadeFrom = 0f;
            _volumeFadeTo = 0f;
        }

        private static float GetSessionTargetVolume(BetterAudioSession session)
        {
            return Mathf.Clamp01(session?.ResolvedAudio?.Volume ?? 1f);
        }

        private void StopAndRefreshChannel(string reason)
        {
            if (_session != null)
            {
                _session.IsCancelled = true;
            }
            _session = null;
            ++_tokenCounter;
            CancelVolumeFade(false);

            StopAndClearPluginSource();
            StopAllSoundEffects(reason);
            ClearPendingAudioAdvance(null);
            _lyricsOverlay?.Destroy();
            _holdToSkipOverlay?.Destroy();
            _holdToSkipOverlay = new HoldToSkipOverlay();
            _holdOwner = null;
            _holdProgress = 0f;
            ClearScheduledAdvance();
            ReleaseGameMusicSuppression();
            InvalidateAudioSource(reason);

            Plugin.LogEffectSuccess("已停止并刷新插件音乐与音效播放通道。");
        }

        private BetterAudioSession EndActiveSessionInternal(
            string reason,
            bool clearLyrics,
            bool restoreGameMusic,
            bool resetPluginChannel)
        {
            BetterAudioSession oldSession = _session;
            if (oldSession != null)
            {
                oldSession.IsCancelled = true;
                _session = null;
                ++_tokenCounter;
            }

            CancelVolumeFade(false);
            StopAndClearPluginSource();

            if (clearLyrics)
            {
                _lyricsOverlay?.Destroy();
            }

            ClearScheduledAdvance();
            ClearHoldState(oldSession?.OwnerView);

            if (restoreGameMusic)
            {
                ReleaseGameMusicSuppression();
            }

            if (resetPluginChannel)
            {
                InvalidateAudioSource(reason);
            }

            return oldSession;
        }

        public void Shutdown()
        {
            EndActiveSessionInternal("Shutdown", true, true, false);
            StopAllSoundEffects("Shutdown");
            ClearPendingAudioAdvance(null);
            ClearScheduledAdvance();
            _audioClipWaiters.Clear();
            _audioClipCache.Clear();
            _holdToSkipOverlay?.Destroy();
            _lyricsOverlay?.Destroy();
            InvalidateAudioSource("Shutdown");

            if (ReferenceEquals(Instance, this))
            {
                Instance = null;
            }
        }

        private static bool HasReachedPlaybackEnd(
            BetterAudioSession session,
            AudioSource source)
        {
            return session.HasPlaybackEndBoundary &&
                   source.time >= session.PlaybackEndSeconds - 0.01f;
        }

        private void HandlePlaybackEnded(BetterAudioSession session, AudioSource source)
        {
            if (_session == null || !ReferenceEquals(_session, session) || session.IsCancelled)
            {
                return;
            }

            if (session.ShouldLoop)
            {
                RestartLoop(session, source);
                return;
            }

            if (session.UsesRepeatCount && session.RepeatCount > 0)
            {
                session.CompletedPlayCount++;
                if (session.CompletedPlayCount < session.RepeatCount)
                {
                    RestartLoop(session, source);
                    return;
                }
            }

            CompleteOneShot(session);
        }

        private void RestartLoop(BetterAudioSession session, AudioSource source)
        {
            if (_session == null || !ReferenceEquals(_session, session) || session.IsPaused)
            {
                return;
            }

            ResetLyrics(session);
            source.loop = session.ShouldLoop && !session.UsesManualSegmentLoop;
            source.time = Mathf.Clamp(
                session.PlaybackStartSeconds,
                0f,
                Mathf.Max(0f, source.clip.length - 0.01f));
            source.Play();
        }

        private void UpdateLyrics(BetterAudioSession session, AudioSource source)
        {
            if (!session.ShowLyrics || session.Lyrics == null || session.Lyrics.Count == 0)
            {
                return;
            }

            float currentTime = source.time;
            if (session.LastPlaybackTime >= 0f && currentTime + 0.05f < session.LastPlaybackTime)
            {
                ResetLyrics(session);
            }
            session.LastPlaybackTime = currentTime;

            int nextIndex = FindLyricIndexAtTime(session.Lyrics, currentTime);
            if (nextIndex == session.LyricIndex)
            {
                return;
            }

            session.LyricIndex = nextIndex;
            RenderLyricAtIndex(session, nextIndex, true);
        }

        private void RenderLyricAtIndex(
            BetterAudioSession session,
            int lyricIndex,
            bool animate)
        {
            if (session == null || lyricIndex < 0 ||
                session.Lyrics == null || lyricIndex >= session.Lyrics.Count)
            {
                _lyricsOverlay?.Clear();
                return;
            }

            LrcLine line = session.Lyrics[lyricIndex];
            if (!session.IsSinging)
            {
                // 背景型音乐会正确识别 idN 和双语 LRC，但忽略演唱角色语义。
                _lyricsOverlay?.ShowLine(line, null, -1, animate);
                return;
            }

            SingingRoleInfo singer;
            if (line.SingerSlot <= 0 ||
                session.SingingRoles == null ||
                !session.SingingRoles.TryGetValue(line.SingerSlot, out singer))
            {
                singer = SingingRoleResolver.Choir(line.SingerSlot);
            }

            _lyricsOverlay?.ShowLine(
                line,
                singer.Name,
                singer.InternalColorMode,
                animate);
        }

        private static int FindLyricIndexAtTime(IList<LrcLine> lyrics, float currentTime)
        {
            if (lyrics == null || lyrics.Count == 0)
            {
                return -1;
            }

            int index = -1;
            while (index + 1 < lyrics.Count &&
                   lyrics[index + 1].TimeSeconds <= currentTime + 0.01f)
            {
                index++;
            }
            return index;
        }

        private void ResetLyrics(BetterAudioSession session)
        {
            session.LyricIndex = -1;
            session.LastPlaybackTime = -1f;
            _lyricsOverlay?.Clear();
        }

        private void AttachLyricsIfPossible(BetterAudioSession session, BaseView owner)
        {
            if (session == null || !session.ShowLyrics ||
                session.Lyrics == null || session.Lyrics.Count == 0)
            {
                _lyricsOverlay?.Destroy();
                return;
            }

            if (owner is NewTalkUI talkUi)
            {
                if (session.LyricsUiState == null)
                {
                    session.LyricsUiState = new FloatingLyricsRuntimeState(
                        session.LyricSizeMode,
                        session.LyricColorMode,
                        session.IsSinging,
                        _lyricsPositionLockState);
                }

                int previousIndex = session.LyricIndex;
                _lyricsOverlay?.Attach(
                    talkUi,
                    session.LyricsUiState);

                // 暂停或加载状态按原设计不显示歌词。播放中的背景会话跨 Talk 时，
                // 同一句歌词无动画恢复；只有确实跨入下一句时才保留浮动淡入动画。
                if (session.IsLoading || session.IsPaused || !session.IsPlaying)
                {
                    return;
                }

                AudioSource source = GetUsableAudioSource();
                if (source == null || source.clip == null)
                {
                    return;
                }

                float currentTime = source.time;
                int currentIndex = FindLyricIndexAtTime(session.Lyrics, currentTime);
                session.LyricIndex = currentIndex;
                session.LastPlaybackTime = currentTime;
                RenderLyricAtIndex(
                    session,
                    currentIndex,
                    currentIndex != previousIndex);
            }
            else
            {
                _lyricsOverlay?.Destroy();
            }
        }

        private void DisableFastForwardNow(BaseView owner)
        {
            if (Time.timeScale <= 1.001f)
            {
                return;
            }

            if (!(owner is NewTalkView runtimeView))
            {
                Game.TimeChange(1f);
                return;
            }

            try
            {
                runtimeView.SpeedUp(false);
            }
            catch
            {
                Game.TimeChange(1f);
            }
        }

        private void AcquireGameMusicSuppression()
        {
            if (!_gameMusicSuppressionHeld)
            {
                _gameMusicSuppressionHeld = true;
                _resumeGameMusicWhenReleased = false;
                _suppressedGameMusicSource = null;
                _gameMusicWarningLogged = false;
            }

            MaintainGameMusicSuppression();
        }

        private void MaintainGameMusicSuppression()
        {
            if (!_gameMusicSuppressionHeld)
            {
                return;
            }

            try
            {
                AudioSource gameMusicSource = TryGetGameMusicSource();
                if (gameMusicSource == null || !gameMusicSource.isPlaying)
                {
                    return;
                }

                AudioMgrEx.PauseMusic();
                _resumeGameMusicWhenReleased = true;
                _suppressedGameMusicSource = gameMusicSource;

            }
            catch (Exception ex)
            {
                if (!_gameMusicWarningLogged)
                {
                    _gameMusicWarningLogged = true;
                    Plugin.LogEffectError(
                        $"插件音乐无法取得原版音乐通道优先级：{ex.Message}");
                }
            }
        }

        private void ReleaseGameMusicSuppression()
        {
            if (!_gameMusicSuppressionHeld && !_resumeGameMusicWhenReleased)
            {
                return;
            }

            bool shouldResume = _resumeGameMusicWhenReleased;
            AudioSource suppressedSource = _suppressedGameMusicSource;

            // 先清状态，防止 UnPause 过程中触发的游戏逻辑再次被误判为仍受插件压制。
            _gameMusicSuppressionHeld = false;
            _resumeGameMusicWhenReleased = false;
            _suppressedGameMusicSource = null;
            _gameMusicWarningLogged = false;

            if (!shouldResume)
            {
                return;
            }

            try
            {
                AudioMgrEx.UnPauseMusic();
            }
            catch (Exception ex)
            {
                // AudioMgr 包装层异常时，尝试直接归还当初被插件暂停的独立原版源。
                try
                {
                    if (suppressedSource != null)
                    {
                        suppressedSource.UnPause();
                        return;
                    }
                }
                catch (Exception fallbackEx)
                {
                    Plugin.LogEffectError(
                        $"原版音乐通道恢复失败：{ex.Message}；后备恢复同样失败：{fallbackEx.Message}");
                    return;
                }

                Plugin.LogEffectError($"原版音乐通道恢复失败：{ex.Message}");
            }
        }

        private static AudioSource TryGetGameMusicSource()
        {
            try
            {
                AudioMgr audioMgr = AudioMgr.Ins;
                Channel channel = audioMgr?.GetChannel(1);
                return channel?.source;
            }
            catch
            {
                return null;
            }
        }

        private AudioSource EnsureAudioSource(string reason, bool forceRecreate = false)
        {
            if (!forceRecreate)
            {
                AudioSource existing = GetUsableAudioSource();
                if (existing != null)
                {
                    return existing;
                }
            }

            if (forceRecreate || _audioSource != null || _audioObject != null)
            {
                InvalidateAudioSource(reason);
            }

            try
            {
                _audioObject = new GameObject("lf-BetterAudioAudioSource");
                DontDestroyOnLoad(_audioObject);
                _audioSource = _audioObject.AddComponent<AudioSource>();
                _audioSource.playOnAwake = false;
                _audioSource.loop = false;
                _audioSource.spatialBlend = 0f;
                _audioSource.volume = 1f;
                return _audioSource;
            }
            catch
            {
                _audioSource = null;
                _audioObject = null;
                return null;
            }
        }

        private AudioSource GetUsableAudioSource()
        {
            try
            {
                return _audioSource != null && _audioSource.gameObject != null
                    ? _audioSource
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private void StopAndClearPluginSource()
        {
            AudioSource source = GetUsableAudioSource();
            if (source == null)
            {
                return;
            }

            try
            {
                source.Stop();
                source.loop = false;
                source.clip = null;
            }
            catch
            {
                InvalidateAudioSource("清理失败");
            }
        }

        private void InvalidateAudioSource(string reason)
        {
            AudioSource source = _audioSource;
            GameObject audioObject = _audioObject;
            _audioSource = null;
            _audioObject = null;

            try
            {
                if (audioObject != null)
                {
                    Destroy(audioObject);
                }
                else if (source != null)
                {
                    Destroy(source);
                }
            }
            catch
            {
                // Unity native object 已失效时直接丢弃引用。
            }
        }

        private void TryBindGameMusicMixer(AudioSource source)
        {
            if (source == null)
            {
                return;
            }

            try
            {
                AudioMgr audioMgr = AudioMgr.Ins;
                Channel channel = audioMgr?.GetChannel(1);
                AudioSource gameSource = channel?.source;
                if (gameSource != null)
                {
                    source.outputAudioMixerGroup = gameSource.outputAudioMixerGroup;
                }
            }
            catch
            {
                // Mixer 绑定失败不影响独立 AudioSource 播放。
            }
        }

        private static bool IsOwnedSingleTalkSession(
            BetterAudioSession session,
            BaseView owner)
        {
            return session != null && !session.IsCancelled &&
                   session.Scope == BetterAudioPlaybackScope.SingleTalk &&
                   ReferenceEquals(session.OwnerView, owner);
        }

        private static bool IsBlockingSingleTalkSession(
            BetterAudioSession session,
            BaseView owner)
        {
            return IsOwnedSingleTalkSession(session, owner) &&
                   !session.IsTransitionTail;
        }

        private static bool IsOwnerAlive(BaseView owner)
        {
            return owner != null && owner.gameObject != null && owner.gameObject.activeInHierarchy;
        }

        private static bool IsControllerUsable(BetterAudioController controller)
        {
            if (controller == null)
            {
                return false;
            }

            try
            {
                return controller.gameObject != null;
            }
            catch
            {
                return false;
            }
        }

        private void OnApplicationQuit()
        {
            _applicationQuitting = true;
            Plugin.Instance?.NotifyApplicationQuitFromPersistentController();
            Shutdown();
        }

        private void OnDestroy()
        {
            try
            {
                if (_session != null || _soundEffectSessions.Count > 0 ||
                    _gameMusicSuppressionHeld || _audioSource != null || _audioObject != null)
                {
                    Shutdown();
                }
            }
            catch
            {
                // Unity 正在退出或销毁 native object 时只做 best-effort 清理。
            }

            if (ReferenceEquals(Instance, this))
            {
                Instance = null;
            }
        }
    }
}
