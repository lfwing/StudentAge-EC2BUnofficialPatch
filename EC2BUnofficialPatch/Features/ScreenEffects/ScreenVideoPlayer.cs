using System;
using EC2BUnofficialPatch.Core;
using Sdk;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Video;
using View.Evt;

namespace EC2BUnofficialPatch.Features.ScreenEffects
{
    internal sealed class ScreenVideoPlayer : MonoBehaviour
    {
        private const int SortingOrder = 30000;
        private static ScreenVideoPlayer _instance;

        private GameObject _host;
        private Canvas _canvas;
        private Image _blocker;
        private Image _backdrop;
        private RawImage _image;
        private AspectRatioFitter _fitter;
        private VideoPlayer _player;
        private RenderTexture _texture;
        private RegisteredScreenVideo _current;
        private bool _embedded;
        private bool _seenRunningWorld;
        private float _skipArmedAt;

        internal static bool IsPlaying => _instance != null && _instance._current != null;

        internal static void Play(RegisteredScreenVideo video)
        {
            if (video == null) return;
            ScreenVideoPlayer player = Ensure();
            if (player == null) return;
            player.Begin(video);
        }

        internal static void Stop(string reason)
        {
            if (_instance == null || _instance._current == null) return;
            _instance.End(reason);
        }

        private static ScreenVideoPlayer Ensure()
        {
            if (_instance != null) return _instance;
            try
            {
                var host = new GameObject("EC2BUnofficialPatch_ScreenVideo") { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(host);
                _instance = host.AddComponent<ScreenVideoPlayer>();
                _instance.Build(host);
                return _instance;
            }
            catch (Exception exception)
            {
                PatchLog.Warning("1164屏幕视频扩展-创建播放层失败：" + ModuleHost.GetReason(exception));
                return null;
            }
        }

        private void Build(GameObject host)
        {
            _host = host;
            _canvas = host.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = SortingOrder;
            host.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            host.AddComponent<GraphicRaycaster>();

            var blockerObject = new GameObject("Blocker");
            blockerObject.transform.SetParent(host.transform, false);
            _blocker = blockerObject.AddComponent<Image>();
            _blocker.color = new Color(0f, 0f, 0f, 0f);
            _blocker.raycastTarget = true;
            Stretch(blockerObject.GetComponent<RectTransform>());

            _player = host.AddComponent<VideoPlayer>();
            _player.playOnAwake = false;
            _player.waitForFirstFrame = true;
            _player.skipOnDrop = true;
            _player.source = VideoSource.Url;
            _player.renderMode = VideoRenderMode.RenderTexture;
            _player.audioOutputMode = VideoAudioOutputMode.Direct;
            _player.prepareCompleted += OnPrepared;
            _player.loopPointReached += OnLoopPoint;
            _player.errorReceived += OnError;

            EnsureVisuals();
            _canvas.enabled = false;
        }

        private void EnsureVisuals()
        {
            if (_backdrop == null)
            {
                var backdropObject = new GameObject("VideoBackdrop");
                backdropObject.transform.SetParent(_host.transform, false);
                _backdrop = backdropObject.AddComponent<Image>();
                _backdrop.color = Color.black;
                _backdrop.raycastTarget = false;
                Stretch(backdropObject.GetComponent<RectTransform>());
                _backdrop.enabled = false;
            }

            if (_image == null)
            {
                var imageObject = new GameObject("VideoImage");
                imageObject.transform.SetParent(_host.transform, false);
                _image = imageObject.AddComponent<RawImage>();
                _image.color = Color.white;
                _image.raycastTarget = false;
                _fitter = imageObject.AddComponent<AspectRatioFitter>();
                Stretch(imageObject.GetComponent<RectTransform>());
                _image.enabled = false;
            }

            _blocker.transform.SetAsLastSibling();
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        private void Begin(RegisteredScreenVideo video)
        {
            if (_current != null) End("replaced");
            EnsureVisuals();
            _current = video;
            _seenRunningWorld = false;
            _skipArmedAt = Time.unscaledTime + 0.3f;

            _embedded = video.RoleOnTop && TryEmbedBehindRoles();
            _blocker.enabled = video.Block;
            _backdrop.enabled = video.Block || _embedded;
            _image.texture = null;
            _image.enabled = false;
            _canvas.enabled = true;

            _player.isLooping = video.Loop;
            _player.url = video.VideoPath;
            _player.Prepare();
            PatchLog.Info($"1164屏幕视频扩展-开始播放：id={video.Id}, file={video.VideoPath}, loop={video.Loop}, block={video.Block}, roleOnTop={video.RoleOnTop}, embedded={_embedded}");
        }

        private bool TryEmbedBehindRoles()
        {
            try
            {
                NewTalkView view = UIMgr.GetOpeningView<NewTalkView>() ?? (NewTalkView)UIMgr.GetOpeningView<View.Event.CommonTalkView>();
                RectTransform roles = view != null ? view.group_role : null;
                if (roles == null || roles.parent == null)
                {
                    PatchLog.Warning("1164屏幕视频扩展-当前没有打开的对话界面，roleOnTop 无效，按盖住立绘播放");
                    return false;
                }

                Transform parent = roles.parent;
                int index = roles.GetSiblingIndex();
                _backdrop.transform.SetParent(parent, false);
                _backdrop.transform.SetSiblingIndex(index);
                _image.transform.SetParent(parent, false);
                _image.transform.SetSiblingIndex(index + 1);
                Stretch(_backdrop.rectTransform);
                Stretch(_image.rectTransform);
                return true;
            }
            catch (Exception exception)
            {
                PatchLog.Warning("1164屏幕视频扩展-嵌入对话界面失败，按盖住立绘播放：" + ModuleHost.GetReason(exception));
                Detach();
                return false;
            }
        }

        private void Detach()
        {
            if (_backdrop != null)
            {
                _backdrop.transform.SetParent(_host.transform, false);
                Stretch(_backdrop.rectTransform);
            }
            if (_image != null)
            {
                _image.transform.SetParent(_host.transform, false);
                Stretch(_image.rectTransform);
            }
            if (_blocker != null) _blocker.transform.SetAsLastSibling();
        }

        private void OnPrepared(VideoPlayer source)
        {
            if (_current == null || _image == null) return;
            int width = (int)Math.Max(16u, source.width);
            int height = (int)Math.Max(16u, source.height);
            ReleaseTexture();
            _texture = new RenderTexture(width, height, 0);
            _texture.Create();
            _player.targetTexture = _texture;
            _image.texture = _texture;
            _image.enabled = true;

            _fitter.aspectRatio = (float)width / height;
            switch (_current.Scale)
            {
                case "fill": _fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent; break;
                case "stretch": _fitter.aspectMode = AspectRatioFitter.AspectMode.None; Stretch(_image.rectTransform); break;
                default: _fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent; break;
            }
            for (ushort track = 0; track < source.audioTrackCount; track++) source.SetDirectAudioVolume(track, _current.Volume);
            source.Play();
        }

        private void OnLoopPoint(VideoPlayer source)
        {
            if (_current == null || _current.Loop) return;
            End("finished");
        }

        private void OnError(VideoPlayer source, string message)
        {
            PatchLog.Warning($"1164屏幕视频扩展-播放失败：id={_current?.Id}, reason={message}");
            End("error");
        }

        private void Update()
        {
            if (_current == null) return;
            try
            {
                if (_embedded && (_image == null || _backdrop == null))
                {
                    End("talk-view-closed");
                    return;
                }

                GameState state = Game.GetGameState();
                if (state != GameState.Start) _seenRunningWorld = true;
                else if (_seenRunningWorld) { End("world-changed"); return; }

                if (_current.Block && _current.Skippable && Time.unscaledTime >= _skipArmedAt && SkipPressed())
                {
                    End("skipped");
                }
            }
            catch (Exception exception)
            {
                PatchLog.Warning("1164屏幕视频扩展-更新失败，已停止：" + ModuleHost.GetReason(exception));
                End("update-exception");
            }
        }

        private static bool SkipPressed()
        {
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) return true;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame || keyboard.escapeKey.wasPressedThisFrame)) return true;
            return false;
        }

        private void End(string reason)
        {
            RegisteredScreenVideo finished = _current;
            _current = null;
            try
            {
                if (_player != null && _player.isPlaying) _player.Stop();
                if (_player != null) _player.targetTexture = null;
            }
            catch (Exception exception)
            {
                PatchLog.Warning("1164屏幕视频扩展-停止播放时异常：" + ModuleHost.GetReason(exception));
            }
            if (_image != null) { _image.texture = null; _image.enabled = false; }
            if (_backdrop != null) _backdrop.enabled = false;
            if (_embedded) Detach();
            _embedded = false;
            _blocker.enabled = false;
            _canvas.enabled = false;
            ReleaseTexture();
            if (finished != null) PatchLog.Info($"1164屏幕视频扩展-结束：id={finished.Id}, reason={reason}");
        }

        private void ReleaseTexture()
        {
            if (_texture == null) return;
            _texture.Release();
            Destroy(_texture);
            _texture = null;
        }

        private void OnDestroy()
        {
            if (_current != null) End("destroyed");
            if (_instance == this) _instance = null;
        }
    }
}
