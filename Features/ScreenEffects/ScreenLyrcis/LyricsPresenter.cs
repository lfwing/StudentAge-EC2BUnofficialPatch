using System;
using System.Runtime.CompilerServices;
using Components;
using DG.Tweening;
using EC2BUnofficialPatch.Core;
using GenUI.Talk;
using Sdk;
using TMPro;
using UnityEngine;

namespace EC2BUnofficialPatch.Features.ScreenEffects.ScreenLyrcis
{
    internal static class LyricsPresenter
    {
        private const float AudioLoadTimeoutSeconds = 30f;
        private static readonly ConditionalWeakTable<object, LyricsState> States =
            new ConditionalWeakTable<object, LyricsState>();

        internal static bool IsActive(object talkView)
        {
            return States.TryGetValue(talkView, out LyricsState state) && state.Active;
        }

        internal static bool TryStart(
            object talkView,
            NewTalkUI ui,
            LyricEntry entry,
            bool preview,
            Action prepare,
            Action completed)
        {
            if (entry == null || ui == null)
            {
                return false;
            }

            PerformanceAudioPlan audioPlan = null;
            if (entry.audio.HasValue &&
                !PerformanceAudioPlan.TryResolve(
                    entry.audio.Value,
                    preview,
                    out audioPlan,
                    out string audioError))
            {
                PatchLog.Error(
                    $"5002屏幕滚动歌词扩展-歌词 id={entry.id} 的 audio 无效：{audioError}");
                return false;
            }

            LyricsState state = States.GetOrCreateValue(talkView);
            try
            {
                StopAndRestore(state, ui);
                state.Snapshot = LyricsSnapshot.Capture(ui);
                state.Generation++;
                int generation = state.Generation;

                prepare();
                state.Active = true;

                // BetterAudio 先暂停并归还它对原版 BGM 的抑制，再按需暂停原版音乐。
                state.BetterAudioLease = BetterAudioBridge.PauseActiveMusic();
                if (audioPlan != null)
                {
                    PauseOriginalMusic(state);
                }

                PrepareUi(ui, entry, out float travel);
                EventMgr.Send(10003);

                if (audioPlan == null)
                {
                    AttachAudioEffect(
                        state,
                        ui,
                        TryGetNpcAudioSource(),
                        TalkViewAccessor.GetLayerOrder(talkView));
                    StartAnimation(
                        state,
                        ui,
                        generation,
                        travel,
                        ComputeSilentDuration(entry, travel),
                        completed);
                    return true;
                }

                state.LoadTimeoutTween = DOVirtual
                    .DelayedCall(AudioLoadTimeoutSeconds, () =>
                    {
                        if (!state.Active || state.Generation != generation)
                        {
                            return;
                        }

                        PatchLog.Error(
                            $"5002屏幕滚动歌词扩展-audio={audioPlan.Id} 加载超时，演出已安全结束。");
                        StopAndRestore(state, ui);
                        completed();
                    }, false);

                ResMgr.LoadAudioAsync(
                    audioPlan.Path,
                    clip => OnAudioLoaded(
                        talkView,
                        ui,
                        entry,
                        audioPlan,
                        clip,
                        state,
                        generation,
                        travel,
                        completed),
                    null,
                    false);
                return true;
            }
            catch (Exception exception)
            {
                PatchLog.Error(
                    $"5002屏幕滚动歌词扩展-启动歌词 id={entry.id} 失败：{ModuleHost.GetReason(exception)}");
                StopAndRestore(state, ui);
                return false;
            }
        }

        internal static void BeforeHide(object talkView, NewTalkUI ui)
        {
            if (States.TryGetValue(talkView, out LyricsState state))
            {
                StopAndRestore(state, ui);
            }
        }

        internal static void Release(object talkView, NewTalkUI ui)
        {
            if (States.TryGetValue(talkView, out LyricsState state))
            {
                StopAndRestore(state, ui);
            }
        }

        private static void PrepareUi(NewTalkUI ui, LyricEntry entry, out float travel)
        {
            ui.scroll_lyrics.gameObject.SetActive(true);
            ui.group_role.gameObject.SetActive(false);
            ui.group_talk.gameObject.SetActive(false);
            ui.canvasgroup_lyrics.DOKill(false);
            ui.canvasgroup_lyrics.alpha = 1f;
            ui.lyrics_content.DOKill(false);

            ApplyEntry(ui, entry);

            float viewportHeight = Mathf.Max(
                ui.Viewport.rect.height,
                ui.transform.rect.height);
            float preferredHeight = Mathf.Max(
                ui.txtex_lyrics.preferredHeight + 100f,
                viewportHeight);
            ui.txtex_lyrics.rectTransform.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                preferredHeight);
            ui.lyrics_content.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                preferredHeight);

            travel = Mathf.Max(0f, preferredHeight - viewportHeight);
            Vector2 position = ui.lyrics_content.anchoredPosition;
            position.y = 0f;
            ui.lyrics_content.anchoredPosition = position;
        }

        private static void OnAudioLoaded(
            object talkView,
            NewTalkUI ui,
            LyricEntry entry,
            PerformanceAudioPlan audioPlan,
            AudioClip clip,
            LyricsState state,
            int generation,
            float travel,
            Action completed)
        {
            if (!state.Active || state.Generation != generation)
            {
                return;
            }

            state.LoadTimeoutTween?.Kill(false);
            state.LoadTimeoutTween = null;

            if (clip == null)
            {
                PatchLog.Error(
                    $"5002屏幕滚动歌词扩展-audio={audioPlan.Id} 加载失败，歌词 id={entry.id} 演出已结束。");
                StopAndRestore(state, ui);
                completed();
                return;
            }

            try
            {
                GameObject audioObject = new GameObject($"EC2B-5002-Audio-{audioPlan.Id}")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                AudioSource source = audioObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0f;
                source.clip = clip;
                source.volume = Mathf.Clamp01(audioPlan.Volume);
                TryBindMusicMixer(source);

                state.AudioObject = audioObject;
                state.AudioSource = source;
                source.Play();

                AttachAudioEffect(
                    state,
                    ui,
                    source,
                    TalkViewAccessor.GetLayerOrder(talkView));
                StartAnimation(
                    state,
                    ui,
                    generation,
                    travel,
                    ComputeAudioDuration(clip.length, travel),
                    completed);
            }
            catch (Exception exception)
            {
                PatchLog.Error(
                    $"5002屏幕滚动歌词扩展-audio={audioPlan.Id} 播放失败：{ModuleHost.GetReason(exception)}");
                StopAndRestore(state, ui);
                completed();
            }
        }

        private static void StartAnimation(
            LyricsState state,
            NewTalkUI ui,
            int generation,
            float travel,
            float duration,
            Action completed)
        {
            duration = Mathf.Max(1f, duration);
            if (travel > 0.01f)
            {
                state.ScrollTween = ui.lyrics_content
                    .DOAnchorPosY(travel, duration, false)
                    .SetEase(Ease.Linear);
            }

            float fadeDuration = Mathf.Clamp(duration * 0.18f, 2f, 10f);
            state.FadeTween = ui.canvasgroup_lyrics
                .DOFade(0f, fadeDuration)
                .SetDelay(Mathf.Max(duration - fadeDuration, 0f))
                .OnComplete(() =>
                {
                    if (!state.Active || state.Generation != generation)
                    {
                        return;
                    }

                    StopAndRestore(state, ui);
                    completed();
                });
        }

        private static float ComputeAudioDuration(float audioLength, float travel)
        {
            float sanitizedLength = float.IsNaN(audioLength) || float.IsInfinity(audioLength)
                ? 0f
                : Mathf.Max(0f, audioLength);
            float adaptiveTail = Mathf.Clamp(2f + travel / 600f, 3f, 12f);
            return Mathf.Clamp(sanitizedLength + adaptiveTail, 8f, 600f);
        }

        private static float ComputeSilentDuration(LyricEntry entry, float travel)
        {
            string text = entry?.text ?? string.Empty;
            int lines = 1;
            foreach (char character in text)
            {
                if (character == '\n')
                {
                    lines++;
                }
            }

            float readingTime = 7f + text.Length * 0.055f + lines * 0.65f;
            float scrollingTime = 8f + travel / 38f;
            return Mathf.Clamp(Mathf.Max(readingTime, scrollingTime), 12f, 240f);
        }

        private static void ApplyEntry(NewTalkUI ui, LyricEntry entry)
        {
            TextMeshProUGUI text = ui.txtex_lyrics;
            if (entry.PreserveExistingStyle)
            {
                text.gameObject.SetActive(true);
                text.alpha = 1f;
                text.ForceMeshUpdate(true, true);
                return;
            }

            text.text = entry.text;
            text.fontSize = entry.fontSize > 0f ? entry.fontSize : 50f;
            text.lineSpacing = entry.lineSpacing > 0f ? entry.lineSpacing : 50f;
            text.alignment = (TextAlignmentOptions)(
                (entry.alignH > 0 ? entry.alignH : 2) |
                (entry.alignV > 0 ? entry.alignV : 256));

            if (!string.IsNullOrWhiteSpace(entry.fontColor) &&
                ColorUtility.TryParseHtmlString(entry.fontColor, out Color color))
            {
                text.color = color;
            }
            else
            {
                text.color = Color.white;
            }

            text.gameObject.SetActive(true);
            text.alpha = 1f;
            text.ForceMeshUpdate(true, true);
        }

        private static void PauseOriginalMusic(LyricsState state)
        {
            try
            {
                AudioSource source = AudioMgr.Ins?.GetChannel(1)?.source;
                if (source == null || !source.isPlaying)
                {
                    return;
                }

                source.Pause();
                state.OriginalMusicSource = source;
                state.ResumeOriginalMusic = true;
            }
            catch (Exception exception)
            {
                PatchLog.Warning(
                    $"5002屏幕滚动歌词扩展-暂停原版音乐失败：{ModuleHost.GetReason(exception)}");
            }
        }

        private static AudioSource TryGetNpcAudioSource()
        {
            try
            {
                AudioSource source = AudioMgr.Ins?.GetChannel(3)?.source;
                return source != null && source.isPlaying ? source : null;
            }
            catch
            {
                return null;
            }
        }

        private static void TryBindMusicMixer(AudioSource source)
        {
            try
            {
                AudioSource gameSource = AudioMgr.Ins?.GetChannel(1)?.source;
                if (gameSource != null)
                {
                    source.outputAudioMixerGroup = gameSource.outputAudioMixerGroup;
                }
            }
            catch
            {
                // Mixer 不可用不影响独立演出音频。
            }
        }

        private static void AttachAudioEffect(
            LyricsState state,
            NewTalkUI ui,
            AudioSource source,
            int layerOrder)
        {
            GameObject effectObject = ui.YuranEffect?.gameObject;
            if (effectObject == null)
            {
                return;
            }

            InkParticleAudioReact react =
                effectObject.GetComponent<InkParticleAudioReact>() ??
                effectObject.AddComponent<InkParticleAudioReact>();
            if (state.AudioReact == null)
            {
                state.AudioReact = react;
                state.OriginalReactSource = react.audioSource;
            }
            react.audioSource = source;

            Renderer renderer = effectObject
                .GetComponent<ParticleSystem>()?
                .GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sortingOrder = layerOrder + 2;
            }
        }

        private static void StopAndRestore(LyricsState state, NewTalkUI ui)
        {
            state.Generation++;
            state.ScrollTween?.Kill(false);
            state.FadeTween?.Kill(false);
            state.LoadTimeoutTween?.Kill(false);
            state.ScrollTween = null;
            state.FadeTween = null;
            state.LoadTimeoutTween = null;

            if (state.AudioReact != null)
            {
                state.AudioReact.audioSource = state.OriginalReactSource;
                state.AudioReact = null;
                state.OriginalReactSource = null;
            }

            if (state.AudioSource != null)
            {
                try
                {
                    state.AudioSource.Stop();
                    state.AudioSource.clip = null;
                }
                catch
                {
                    // Unity 对象可能已随界面销毁。
                }
            }

            if (state.AudioObject != null)
            {
                UnityEngine.Object.Destroy(state.AudioObject);
            }
            state.AudioSource = null;
            state.AudioObject = null;

            if (state.Snapshot != null)
            {
                state.Snapshot.Restore(ui);
                state.Snapshot = null;
            }

            if (state.ResumeOriginalMusic && state.OriginalMusicSource != null)
            {
                try
                {
                    if (!state.OriginalMusicSource.isPlaying)
                    {
                        state.OriginalMusicSource.UnPause();
                    }
                }
                catch
                {
                    // 原版 AudioSource 已失效时不再强行恢复。
                }
            }
            state.ResumeOriginalMusic = false;
            state.OriginalMusicSource = null;

            BetterAudioPauseLease lease = state.BetterAudioLease;
            state.BetterAudioLease = null;
            BetterAudioBridge.Resume(lease);
            state.Active = false;
        }

        private sealed class LyricsState
        {
            internal bool Active;
            internal int Generation;
            internal LyricsSnapshot Snapshot;
            internal Tween ScrollTween;
            internal Tween FadeTween;
            internal Tween LoadTimeoutTween;
            internal GameObject AudioObject;
            internal AudioSource AudioSource;
            internal AudioSource OriginalMusicSource;
            internal bool ResumeOriginalMusic;
            internal BetterAudioPauseLease BetterAudioLease;
            internal InkParticleAudioReact AudioReact;
            internal AudioSource OriginalReactSource;
        }

        private sealed class LyricsSnapshot
        {
            private readonly string _text;
            private readonly float _fontSize;
            private readonly float _lineSpacing;
            private readonly Color _color;
            private readonly TextAlignmentOptions _alignment;
            private readonly Vector2 _textSize;
            private readonly Vector2 _contentSize;
            private readonly Vector2 _contentPosition;
            private readonly float _canvasAlpha;
            private readonly bool _scrollActive;
            private readonly bool _roleActive;
            private readonly bool _talkActive;

            private LyricsSnapshot(NewTalkUI ui)
            {
                _text = ui.txtex_lyrics.text;
                _fontSize = ui.txtex_lyrics.fontSize;
                _lineSpacing = ui.txtex_lyrics.lineSpacing;
                _color = ui.txtex_lyrics.color;
                _alignment = ui.txtex_lyrics.alignment;
                _textSize = ui.txtex_lyrics.rectTransform.sizeDelta;
                _contentSize = ui.lyrics_content.sizeDelta;
                _contentPosition = ui.lyrics_content.anchoredPosition;
                _canvasAlpha = ui.canvasgroup_lyrics.alpha;
                _scrollActive = ui.scroll_lyrics.gameObject.activeSelf;
                _roleActive = ui.group_role.gameObject.activeSelf;
                _talkActive = ui.group_talk.gameObject.activeSelf;
            }

            internal static LyricsSnapshot Capture(NewTalkUI ui)
            {
                return new LyricsSnapshot(ui);
            }

            internal void Restore(NewTalkUI ui)
            {
                ui.txtex_lyrics.text = _text;
                ui.txtex_lyrics.fontSize = _fontSize;
                ui.txtex_lyrics.lineSpacing = _lineSpacing;
                ui.txtex_lyrics.color = _color;
                ui.txtex_lyrics.alignment = _alignment;
                ui.txtex_lyrics.rectTransform.sizeDelta = _textSize;
                ui.lyrics_content.sizeDelta = _contentSize;
                ui.lyrics_content.anchoredPosition = _contentPosition;
                ui.canvasgroup_lyrics.alpha = _canvasAlpha;
                ui.scroll_lyrics.gameObject.SetActive(_scrollActive);
                ui.group_role.gameObject.SetActive(_roleActive);
                ui.group_talk.gameObject.SetActive(_talkActive);
            }
        }
    }
}
