using System;
using EC2BUnofficialPatch.Core;
using GenUI.Talk;
using HarmonyLib;
using LFBetterAudio.UI;
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
        private const float TransitionDuration = 0.22f;
        private static readonly System.Reflection.FieldInfo RuntimeCgPanel =
            AccessTools.Field(typeof(NewTalkView), "cgPanel");
        private static readonly System.Reflection.FieldInfo PreviewCgPanel =
            AccessTools.Field(typeof(PreviewTalkView), "cgPanel");
        private static ScreenVideoPlayer _instance;

        private GameObject _host;
        private Canvas _canvas;
        private Canvas _debugCanvas;
        private int _debugSortingOrder;
        private int _debugSortingLayerId;
        private Image _blocker;
        private Image _backdrop;
        private CanvasGroup _backdropGroup;
        private RawImage _image;
        private CanvasGroup _imageGroup;
        private RectTransform _cgVideoLayer;
        private Canvas _cgVideoCanvas;
        private bool _cgUsesDialogueSave;
        private Canvas _cgTextCanvas;
        private bool _cgTextCanvasAdded;
        private bool _cgTextCanvasEnabled;
        private bool _cgTextOverrideSorting;
        private int _cgTextSortingOrder;
        private int _cgTextSortingLayerId;
        private AspectRatioFitter _fitter;
        private VideoPlayer _player;
        private RenderTexture _texture;
        private RegisteredScreenVideo _current;
        private PreviewTalkView _previewOwner;
        private bool _embedded;
        private bool _seenRunningWorld;
        private float _skipArmedAt;
        private float _holdStartedAt = -1f;
        private HoldToSkipOverlay _skipOverlay;
        private bool _ending;
        private float _fadeElapsed;
        private float _fadeFrom;
        private string _endReason;
        private RegisteredScreenVideo _pendingVideo;
        private PreviewTalkView _pendingPreviewOwner;

        internal static bool IsPlaying => _instance != null && _instance._current != null;
        internal static bool BlocksInput => IsPlaying && !_instance._current.RoleOnTop;

        internal static void Play(RegisteredScreenVideo video, PreviewTalkView previewOwner = null)
        {
            if (video == null) return;
            ScreenVideoPlayer player = Ensure();
            if (player == null) return;
            player.Begin(video, previewOwner);
        }

        internal static void Stop(string reason)
        {
            if (_instance == null || _instance._current == null) return;
            _instance.End(reason);
        }

        internal static void StopPreview(PreviewTalkView owner)
        {
            if (_instance != null && _instance._pendingPreviewOwner == owner)
            {
                _instance._pendingVideo = null;
                _instance._pendingPreviewOwner = null;
            }
            if (_instance != null && _instance._previewOwner == owner && owner != null)
                _instance.End("preview-closed");
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
                _backdropGroup = backdropObject.AddComponent<CanvasGroup>();
                _backdropGroup.interactable = false;
                _backdropGroup.blocksRaycasts = false;
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
                _imageGroup = imageObject.AddComponent<CanvasGroup>();
                _imageGroup.interactable = false;
                _imageGroup.blocksRaycasts = false;
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

        private void Begin(RegisteredScreenVideo video, PreviewTalkView previewOwner)
        {
            if (_current != null)
            {
                _pendingVideo = video;
                _pendingPreviewOwner = previewOwner;
                End("replaced");
                return;
            }
            EnsureVisuals();
            _current = video;
            _previewOwner = previewOwner;
            _ending = false;
            _fadeElapsed = 0f;
            SetVisualAlpha(0f);
            _seenRunningWorld = false;
            _skipArmedAt = Time.unscaledTime + 0.3f;
            _holdStartedAt = -1f;

            _embedded = video.RoleOnTop && TryEmbedBehindRoles(previewOwner);
            if (video.RoleOnTop && !_embedded)
            {
                _current = null;
                _previewOwner = null;
                return;
            }
            _blocker.enabled = !video.RoleOnTop;
            _backdrop.enabled = !video.RoleOnTop || _embedded;
            _image.texture = null;
            _image.enabled = false;
            _canvas.enabled = true;
            UpdateCanvasOrder();
            PromoteDebugCanvas();
            GameMusicPriority.SetVideo(true);

            _player.isLooping = video.Loop;
            _player.url = video.VideoPath;
            try { _player.Prepare(); }
            catch
            {
                FinishImmediately("prepare-failed");
                throw;
            }
            VideoLog($"1164屏幕视频扩展-开始播放：id={video.Id}, file={video.VideoPath}, loop={video.Loop}, roleOnTop={video.RoleOnTop}, embedded={_embedded}");
        }

        private bool TryEmbedBehindRoles(PreviewTalkView previewOwner)
        {
            try
            {
                NewTalkUI view = previewOwner ?? (NewTalkUI)UIMgr.GetOpeningView<NewTalkView>() ??
                    (NewTalkUI)UIMgr.GetOpeningView<View.Event.CommonTalkView>() ??
                    UIMgr.GetOpeningView<PreviewTalkView>();
                RectTransform roles = view != null ? view.group_role : null;
                if (roles == null || roles.parent == null)
                {
                    PatchLog.Warning("1164屏幕视频扩展-当前没有打开的对话界面，无法播放动态背景视频");
                    return false;
                }

                CGView cg = GetActiveCgView(view);
                RectTransform anchor = cg != null && cg.mask_talk_cg != null && cg.mask_talk_cg.parent != null
                    ? cg.mask_talk_cg
                    : roles;
                if (cg != null && !TryConfigureCgLayers(cg)) return false;
                if (cg == null) RestoreCgLayers();
                Transform parent = cg != null ? _cgVideoLayer : anchor.parent;
                if (_backdrop.transform.parent != parent || _image.transform.parent != parent ||
                    _backdrop.transform.GetSiblingIndex() + 1 != _image.transform.GetSiblingIndex() ||
                    (cg == null && _image.transform.GetSiblingIndex() + 1 != anchor.GetSiblingIndex()))
                {
                    _backdrop.transform.SetParent(parent, false);
                    _image.transform.SetParent(parent, false);
                    if (cg == null)
                    {
                        _backdrop.transform.SetSiblingIndex(anchor.GetSiblingIndex());
                        _image.transform.SetSiblingIndex(anchor.GetSiblingIndex());
                    }
                    else
                    {
                        _backdrop.transform.SetSiblingIndex(0);
                        _image.transform.SetSiblingIndex(1);
                    }
                    Stretch(_backdrop.rectTransform);
                    Stretch(_image.rectTransform);
                }
                return true;
            }
            catch (Exception exception)
            {
                PatchLog.Warning("1164屏幕视频扩展-嵌入对话界面失败，已忽略动态背景视频：" + ModuleHost.GetReason(exception));
                Detach();
                return false;
            }
        }

        private bool TryConfigureCgLayers(CGView cg)
        {
            RectTransform text = cg.mask_talk_cg;
            if (text == null || text.parent == null) return false;
            Canvas dialogueSaveCanvas = FindDialogueSaveCanvas(cg.txtex_talk_cg != null
                ? cg.txtex_talk_cg.transform : null);
            bool dialogueSave = dialogueSaveCanvas != null;
            Transform shield = dialogueSave && text.parent.name == "ADV.NativeShield" ? text.parent : null;
            Transform parent = dialogueSave ? (shield != null ? shield.parent : cg.transform) : text.parent;
            if (parent == null) return false;
            if ((_cgVideoLayer != null && _cgVideoLayer.parent != parent) ||
                (_cgVideoLayer != null && _cgUsesDialogueSave != dialogueSave) ||
                (_cgTextCanvas != null && (_cgTextCanvas.gameObject != text.gameObject || dialogueSave)))
                RestoreCgLayers();

            int imageOrder = int.MinValue;
            int sortingLayer = 0;
            foreach (Canvas canvas in cg.gameObject.GetComponentsInParent<Canvas>(true))
                ConsiderCanvas(canvas, ref imageOrder, ref sortingLayer);
            if (cg.icon_cg != null)
                foreach (Canvas canvas in cg.icon_cg.gameObject.GetComponentsInChildren<Canvas>(true))
                    ConsiderCanvas(canvas, ref imageOrder, ref sortingLayer);
            if (cg.icon_cg_mini != null)
                foreach (Canvas canvas in cg.icon_cg_mini.gameObject.GetComponentsInChildren<Canvas>(true))
                    ConsiderCanvas(canvas, ref imageOrder, ref sortingLayer);
            if (imageOrder >= 32765)
            {
                PatchLog.Warning("1164屏幕视频扩展-CG 图像排序值过高，无法在图像与文字之间建立视频层");
                return false;
            }
            if (imageOrder == int.MinValue) imageOrder = 0;

            int videoOrder = imageOrder + 1;
            if (dialogueSave)
            {
                int imageLayerValue = SortingLayer.GetLayerValueFromID(sortingLayer);
                int textLayerValue = SortingLayer.GetLayerValueFromID(dialogueSaveCanvas.sortingLayerID);
                if (imageLayerValue > textLayerValue ||
                    (imageLayerValue == textLayerValue && videoOrder >= dialogueSaveCanvas.sortingOrder))
                {
                    PatchLog.Warning("1164屏幕视频扩展-DialogueSave 的 ADV 文字层不在 CG 图片之上，无法安全插入视频");
                    return false;
                }
                if (imageLayerValue < textLayerValue)
                {
                    sortingLayer = dialogueSaveCanvas.sortingLayerID;
                    videoOrder = dialogueSaveCanvas.sortingOrder - 1;
                }
            }

            if (_cgVideoLayer == null)
            {
                var layer = new GameObject("ScreenVideo_CgLayer", typeof(RectTransform), typeof(Canvas));
                _cgVideoLayer = layer.GetComponent<RectTransform>();
                _cgVideoLayer.SetParent(parent, false);
                Stretch(_cgVideoLayer);
                _cgVideoCanvas = layer.GetComponent<Canvas>();
                _cgUsesDialogueSave = dialogueSave;
                if (dialogueSave)
                    VideoLog($"1164屏幕视频扩展-检测到 DialogueSave ADV，CG 视频避开原生文字遮罩：imageOrder={imageOrder}, videoOrder={videoOrder}, textOrder={dialogueSaveCanvas.sortingOrder}");
            }
            Transform anchor = dialogueSave ? shield : text;
            if (anchor != null && _cgVideoLayer.GetSiblingIndex() + 1 != anchor.GetSiblingIndex())
            {
                int target = anchor.GetSiblingIndex();
                if (_cgVideoLayer.GetSiblingIndex() < target) target--;
                _cgVideoLayer.SetSiblingIndex(target);
            }
            else if (anchor == null) _cgVideoLayer.SetAsLastSibling();

            if (!dialogueSave && _cgTextCanvas == null)
            {
                _cgTextCanvas = text.GetComponent<Canvas>();
                _cgTextCanvasAdded = _cgTextCanvas == null;
                if (_cgTextCanvasAdded) _cgTextCanvas = text.gameObject.AddComponent<Canvas>();
                else
                {
                    _cgTextCanvasEnabled = _cgTextCanvas.enabled;
                    _cgTextOverrideSorting = _cgTextCanvas.overrideSorting;
                    _cgTextSortingOrder = _cgTextCanvas.sortingOrder;
                    _cgTextSortingLayerId = _cgTextCanvas.sortingLayerID;
                }
                VideoLog($"1164屏幕视频扩展-CG 层级：imageOrder={imageOrder}, textCanvasAdded={_cgTextCanvasAdded}");
            }

            _cgVideoCanvas.sortingLayerID = sortingLayer;
            _cgVideoCanvas.overrideSorting = true;
            _cgVideoCanvas.sortingOrder = videoOrder;
            if (_cgTextCanvas != null)
            {
                _cgTextCanvas.sortingLayerID = sortingLayer;
                _cgTextCanvas.overrideSorting = true;
                _cgTextCanvas.sortingOrder = videoOrder + 1;
                _cgTextCanvas.enabled = true;
            }
            return true;
        }

        private static Canvas FindDialogueSaveCanvas(Transform text)
        {
            for (Transform current = text; current != null; current = current.parent)
            {
                Canvas canvas = current.GetComponent<Canvas>();
                if (canvas != null && current.name == "DialogueSave.ADV") return canvas;
            }
            return null;
        }

        private static void ConsiderCanvas(Canvas canvas, ref int order, ref int layer)
        {
            if (canvas == null || !canvas.enabled) return;
            int canvasLayerValue = SortingLayer.GetLayerValueFromID(canvas.sortingLayerID);
            int currentLayerValue = SortingLayer.GetLayerValueFromID(layer);
            if (order != int.MinValue && (canvasLayerValue < currentLayerValue ||
                (canvasLayerValue == currentLayerValue && canvas.sortingOrder < order))) return;
            order = canvas.sortingOrder;
            layer = canvas.sortingLayerID;
        }

        private void RestoreCgLayers()
        {
            if (_cgVideoLayer != null)
            {
                if (_backdrop != null && _backdrop.transform.parent == _cgVideoLayer)
                    _backdrop.transform.SetParent(_host.transform, false);
                if (_image != null && _image.transform.parent == _cgVideoLayer)
                    _image.transform.SetParent(_host.transform, false);
                Destroy(_cgVideoLayer.gameObject);
            }
            _cgVideoLayer = null;
            _cgVideoCanvas = null;
            _cgUsesDialogueSave = false;
            if (_cgTextCanvas == null) return;
            if (_cgTextCanvasAdded)
            {
                _cgTextCanvas.enabled = false;
                Destroy(_cgTextCanvas);
            }
            else
            {
                _cgTextCanvas.enabled = _cgTextCanvasEnabled;
                _cgTextCanvas.overrideSorting = _cgTextOverrideSorting;
                _cgTextCanvas.sortingOrder = _cgTextSortingOrder;
                _cgTextCanvas.sortingLayerID = _cgTextSortingLayerId;
            }
            _cgTextCanvas = null;
            _cgTextCanvasAdded = false;
        }

        private void Detach()
        {
            RestoreCgLayers();
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
            FinishImmediately("error");
        }

        private void Update()
        {
            if (_current == null) return;
            try
            {
                // DebugMgr creates its own persistent canvas, possibly after playback begins.
                UpdateCanvasOrder();
                if (_embedded && !TryEmbedBehindRoles(_previewOwner))
                {
                    FinishImmediately("talk-view-closed");
                    return;
                }
                PromoteDebugCanvas();

                GameState state = Game.GetGameState();
                if (state != GameState.Start) _seenRunningWorld = true;
                else if (_seenRunningWorld) { FinishImmediately("world-changed"); return; }

                AdvanceFade();
                if (_current == null || _ending) return;
                UpdateSkip();
            }
            catch (Exception exception)
            {
                PatchLog.Warning("1164屏幕视频扩展-更新失败，已停止：" + ModuleHost.GetReason(exception));
                FinishImmediately("update-exception");
            }
        }

        private void UpdateSkip()
        {
            if (_current.RoleOnTop || !_current.Skippable || Time.unscaledTime < _skipArmedAt)
                return;
            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.isPressed)
            {
                _holdStartedAt = -1f;
                _skipOverlay?.Hide();
                return;
            }
            if (_holdStartedAt < 0f)
            {
                _holdStartedAt = Time.unscaledTime;
                NewTalkUI view = UIMgr.GetOpeningView<NewTalkView>() ??
                    (NewTalkUI)UIMgr.GetOpeningView<View.Event.CommonTalkView>() ??
                    _previewOwner;
                if (view != null)
                {
                    if (_skipOverlay == null) _skipOverlay = new HoldToSkipOverlay();
                    _skipOverlay.Show(view, _host.transform);
                }
            }
            float progress = Mathf.Clamp01(Time.unscaledTime - _holdStartedAt);
            _skipOverlay?.SetProgress(progress);
            if (progress >= 1f) End("skipped");
        }

        private void End(string reason)
        {
            if (_current == null) return;
            if (reason == "effect-stop")
            {
                _pendingVideo = null;
                _pendingPreviewOwner = null;
            }
            if (_ending || _texture == null || _image == null || !_image.enabled ||
                _imageGroup.alpha <= 0f)
            {
                if (!_ending) FinishImmediately(reason);
                return;
            }
            _ending = true;
            _endReason = reason;
            _fadeElapsed = 0f;
            _fadeFrom = _imageGroup.alpha;
        }

        private void AdvanceFade()
        {
            if (_texture == null || _image == null || !_image.enabled) return;
            if (!_ending && _embedded && !_image.gameObject.activeInHierarchy) return;
            if (!_ending && _player != null && _player.frame < 0) return;
            _fadeElapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(_fadeElapsed / TransitionDuration);
            SetVisualAlpha(_ending ? _fadeFrom * (1f - progress) : progress);
            if (_ending && progress >= 1f) FinishImmediately(_endReason);
        }

        private void SetVisualAlpha(float alpha)
        {
            if (_backdropGroup != null) _backdropGroup.alpha = alpha;
            if (_imageGroup != null) _imageGroup.alpha = alpha;
        }

        private void FinishImmediately(string reason)
        {
            RegisteredScreenVideo finished = _current;
            _current = null;
            _previewOwner = null;
            _ending = false;
            _endReason = null;
            GameMusicPriority.SetVideo(false);
            _skipOverlay?.Destroy();
            _skipOverlay = null;
            _holdStartedAt = -1f;
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
            _canvas.sortingOrder = SortingOrder;
            RestoreDebugCanvas();
            ReleaseTexture();
            if (finished != null) VideoLog($"1164屏幕视频扩展-结束：id={finished.Id}, reason={reason}");
            RegisteredScreenVideo next = _pendingVideo;
            PreviewTalkView nextOwner = _pendingPreviewOwner;
            _pendingVideo = null;
            _pendingPreviewOwner = null;
            if (next != null && reason != "destroyed") Begin(next, nextOwner);
        }

        private static void VideoLog(string message)
        {
            if (PluginConfig.ScreenVideoLogging?.Value == true) PatchLog.Info(message);
        }

        private void PromoteDebugCanvas()
        {
            if (DebugMgr.Ins == null) return;
            Canvas canvas = _debugCanvas;
            if (canvas == null)
            {
                GameObject debugRoot = GameObject.Find("DebugCanvas");
                canvas = debugRoot != null ? debugRoot.GetComponent<Canvas>() : null;
            }
            Canvas top = _canvas;
            if (_cgTextCanvas != null && IsCanvasAbove(_cgTextCanvas, top)) top = _cgTextCanvas;
            GameObject dialogueModal = GameObject.Find("DialogueSave.ADV.Modal");
            Canvas modalCanvas = dialogueModal != null ? dialogueModal.GetComponent<Canvas>() : null;
            if (modalCanvas != null && IsCanvasAbove(modalCanvas, top)) top = modalCanvas;
            int topOrder = top.sortingOrder;
            int topLayer = top.sortingLayerID;
            if (canvas == null) return;
            int debugLayerValue = SortingLayer.GetLayerValueFromID(canvas.sortingLayerID);
            int topLayerValue = SortingLayer.GetLayerValueFromID(topLayer);
            if (debugLayerValue > topLayerValue ||
                (debugLayerValue == topLayerValue && canvas.sortingOrder > topOrder)) return;
            if (_debugCanvas == null)
            {
                _debugCanvas = canvas;
                _debugSortingOrder = canvas.sortingOrder;
                _debugSortingLayerId = canvas.sortingLayerID;
            }
            if (debugLayerValue < topLayerValue) canvas.sortingLayerID = topLayer;
            canvas.sortingOrder = Math.Min(32767, topOrder + 1);
        }

        private static bool IsCanvasAbove(Canvas candidate, Canvas other)
        {
            int candidateLayer = SortingLayer.GetLayerValueFromID(candidate.sortingLayerID);
            int otherLayer = SortingLayer.GetLayerValueFromID(other.sortingLayerID);
            return candidateLayer > otherLayer ||
                (candidateLayer == otherLayer && candidate.sortingOrder > other.sortingOrder);
        }

        private void UpdateCanvasOrder()
        {
            int order = SortingOrder;
            NewTalkUI view = _previewOwner ?? (NewTalkUI)UIMgr.GetOpeningView<NewTalkView>() ??
                (NewTalkUI)UIMgr.GetOpeningView<View.Event.CommonTalkView>();
            CGView cg = GetActiveCgView(view);
            if (cg != null && cg.gameObject != null && cg.gameObject.activeInHierarchy)
            {
                foreach (Canvas canvas in cg.gameObject.GetComponentsInParent<Canvas>(true))
                    if (canvas != null && canvas.sortingOrder >= order)
                        order = Math.Min(32766, canvas.sortingOrder + 1);
            }
            _canvas.sortingOrder = order;
        }

        private static CGView GetActiveCgView(NewTalkUI view)
        {
            CGView cg = view is PreviewTalkView preview
                ? PreviewCgPanel?.GetValue(preview) as CGView
                : view is NewTalkView runtime ? RuntimeCgPanel?.GetValue(runtime) as CGView : null;
            return cg != null && cg.gameObject != null && cg.gameObject.activeInHierarchy ? cg : null;
        }

        private void RestoreDebugCanvas()
        {
            if (_debugCanvas != null)
            {
                _debugCanvas.sortingOrder = _debugSortingOrder;
                _debugCanvas.sortingLayerID = _debugSortingLayerId;
            }
            _debugCanvas = null;
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
            _pendingVideo = null;
            _pendingPreviewOwner = null;
            if (_current != null) FinishImmediately("destroyed");
            if (_instance == this) _instance = null;
        }
    }
}
