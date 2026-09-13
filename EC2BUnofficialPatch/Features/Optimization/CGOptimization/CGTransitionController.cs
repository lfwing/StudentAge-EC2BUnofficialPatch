using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DG.Tweening;
using EC2BUnofficialPatch.Core;
using HarmonyLib;
using Sdk;
using UnityEngine;
using UnityEngine.UI;

namespace EC2BUnofficialPatch.Features.Optimization.CGOptimization
{
    /// <summary>
    /// 为剧情内全屏 CG 提供异步双层交叉淡化，并在 root_cg 下方维护独立保底层。
    /// 新 CG 加载完成前始终保留上一张完整 CG；过期异步回调不会覆盖最新请求。
    /// </summary>
    internal sealed class CGTransitionController : MonoBehaviour
    {
        private const float CrossFadeDuration = 0.22f;
        private const float SlowZoomTarget = 1.2f;
        private const float SlowZoomDuration = 30f;
        private const float RequestTimeoutSeconds = 20f;

        private static readonly MethodInfo UrlSetter =
            AccessTools.PropertySetter(typeof(UISprite), nameof(UISprite.url));

        private static readonly List<CGTransitionController> Controllers =
            new List<CGTransitionController>();

        private static int _nextControllerId;

        private readonly int _controllerId = ++_nextControllerId;

        private UISprite _sprite;
        private Image _sourceImage;
        private RectTransform _sourceRect;
        private CgLayer _layerA;
        private CgLayer _layerB;
        private CgLayer _frontLayer;
        private CgLayer _backLayer;
        private CGHoldLayer _holdLayer;

        private bool _bound;
        private bool _hasCurrent;
        private bool _requestPending;
        private bool _completedPending;
        private bool _fadeActive;
        private bool _holdCommitPending;
        private long _generation;
        private int _latestCgId = -1;
        private string _latestDisplayUrl;
        private string _latestCanonicalUrl;
        private int _appliedCgId = -1;
        private string _appliedDisplayUrl;
        private string _appliedCanonicalUrl;
        private float _requestStartedRealtime;
        private float _fadeElapsed;
        private int _holdCommitFrame;
        private Sprite _holdCommitSprite;

        private Sprite _completedSprite;
        private int _completedCgId;
        private string _completedDisplayUrl;
        private string _completedCanonicalUrl;
        private long _completedGeneration;

        internal static void BeginExitAll(float duration, string reason)
        {
            ForEachController(controller => controller.BeginExit(duration, reason));
        }

        internal static void ClearAllImmediately(string reason)
        {
            ForEachController(controller => controller.SwitchToOtherMode(reason));
        }

        private static void ForEachController(Action<CGTransitionController> action)
        {
            for (int index = Controllers.Count - 1; index >= 0; index--)
            {
                CGTransitionController controller = Controllers[index];
                if (controller == null)
                {
                    Controllers.RemoveAt(index);
                    continue;
                }

                try
                {
                    action(controller);
                }
                catch (Exception exception)
                {
                    PatchLog.Exception(
                        $"优化模块-CG优化批量生命周期处理失败：controller={controller._controllerId}",
                        exception);
                }
            }
        }

        internal void Bind(UISprite sprite)
        {
            if (sprite == null || sprite.image == null || sprite.transform == null)
            {
                throw new ArgumentNullException(nameof(sprite), "UISprite、Image 或 RectTransform 无效。");
            }

            bool changed = !ReferenceEquals(_sprite, sprite);
            _sprite = sprite;
            _sourceImage = sprite.image;
            _sourceRect = sprite.transform;
            _bound = true;

            RegisterController();
            EnsureLayers();
            EnsureHoldLayer();

            _sourceImage.DOKill(false);
            SetSourceAlpha(1f);
            _sourceImage.enabled = false;
            SetLayersActive(true);
            MirrorSourceVisualState();
            _holdLayer?.CancelExit();

            // CGView 在连续 Show 时可能短暂隐藏 icon_cg；重新绑定后立即用当前完整层刷新保底图。
            if (_hasCurrent && _frontLayer?.Sprite != null)
            {
                CommitHoldImmediately(_frontLayer.Sprite, "重新绑定后恢复当前CG");
            }

            if (changed)
            {
                PatchLog.Debug(
                    "优化模块-CG优化控制器绑定：" +
                    $"controller={_controllerId}, object={gameObject.name}, " +
                    $"sourceSprite={Describe(_sourceImage.sprite)}, holdReady={_holdLayer != null}");
            }
        }

        internal void FallbackToOriginal(string reason)
        {
            if (!_bound)
            {
                return;
            }

            Invalidate("回退原版：" + reason);
            ResetLocalVisualState();
            _holdLayer?.ClearImmediate("回退原版");
            if (_sourceImage != null)
            {
                _sourceImage.enabled = true;
            }

            PatchLog.Error(
                $"优化模块-CG优化已回退原版显示：controller={_controllerId}, reason={reason}");
        }

        internal void Play(int cgId, string displayUrl)
        {
            if (!_bound)
            {
                throw new InvalidOperationException("CGTransitionController 尚未绑定 UISprite。");
            }

            ResolveRequest(displayUrl, out string canonicalUrl, out bool external);
            if (string.IsNullOrEmpty(canonicalUrl))
            {
                PatchLog.Error(
                    $"优化模块-CG优化无法解析 CG URL：controller={_controllerId}, cgId={cgId}, url={displayUrl}");
                return;
            }

            if (external && !File.Exists(canonicalUrl))
            {
                PatchLog.Error(
                    "优化模块-CG优化外部 CG 不存在，继续保持当前 CG：" +
                    $"controller={_controllerId}, cgId={cgId}, path={canonicalUrl}");
                return;
            }

            if (!_requestPending && !_completedPending && !_fadeActive && _hasCurrent &&
                string.Equals(_appliedCanonicalUrl, canonicalUrl, StringComparison.Ordinal))
            {
                CommitHoldImmediately(_frontLayer?.Sprite, "重复请求保持当前CG");
                PatchLog.Debug(
                    "优化模块-CG优化忽略重复 CG 请求：" +
                    $"controller={_controllerId}, cgId={cgId}, url={displayUrl}");
                return;
            }

            EnsureLayers();
            EnsureHoldLayer();
            _holdLayer?.CancelExit();

            if (_hasCurrent && _frontLayer?.Sprite != null)
            {
                CommitHoldImmediately(_frontLayer.Sprite, "新CG加载前保留上一张CG");
            }

            long generation = ++_generation;
            _latestCgId = cgId;
            _latestDisplayUrl = displayUrl;
            _latestCanonicalUrl = canonicalUrl;
            _requestPending = true;
            ClearCompletedResult();
            CancelHoldCommit();
            _requestStartedRealtime = Time.realtimeSinceStartup;

            SetUrlValue(canonicalUrl);
            _sprite.showWhenComp = true;
            _sourceImage.gameObject.SetActive(true);
            _sourceImage.enabled = false;
            SetLayersActive(true);

            PatchLog.Debug(
                "优化模块-CG优化收到剧情 CG 请求：" +
                $"controller={_controllerId}, cgId={cgId}, generation={generation}, " +
                $"external={external}, url={displayUrl}, canonical={canonicalUrl}, " +
                $"keepCurrent={_hasCurrent}, holdVisible={_holdLayer?.HasSprite ?? false}");

            Action<Sprite> callback = loadedSprite =>
                OnLoadCompleted(generation, cgId, displayUrl, canonicalUrl, loadedSprite);

            if (external)
            {
                ResMgr.LoadExternSpriteAsync(canonicalUrl, callback, false);
            }
            else
            {
                ResMgr.LoadSpriteAsync(canonicalUrl, callback, null, false);
            }
        }

        internal void BeginExit(float duration, string reason)
        {
            if (!_bound)
            {
                return;
            }

            Sprite current = GetBestCurrentSprite();
            if (current != null)
            {
                CommitHoldImmediately(current, "退出前固定最后CG");
            }

            Invalidate("开始退出：" + reason);
            ResetLocalVisualState();
            _holdLayer?.FadeOutAndClear(duration, reason);

            PatchLog.Debug(
                "优化模块-CG优化开始同步退出保底层：" +
                $"controller={_controllerId}, duration={duration:F2}, reason={reason}, " +
                $"hasHold={_holdLayer?.HasSprite ?? false}");
        }

        internal void SwitchToOtherMode(string reason)
        {
            if (!_bound)
            {
                return;
            }

            Invalidate("切换其他显示模式：" + reason);
            ResetLocalVisualState();
            _holdLayer?.ClearImmediate(reason);

            PatchLog.Debug(
                $"优化模块-CG优化已清理CG保底层：controller={_controllerId}, reason={reason}");
        }

        private void LateUpdate()
        {
            if (!_bound)
            {
                return;
            }

            MirrorSourceVisualState();
            _holdLayer?.Heartbeat();

            if (_requestPending &&
                Time.realtimeSinceStartup - _requestStartedRealtime > RequestTimeoutSeconds)
            {
                _requestPending = false;
                PatchLog.Error(
                    "优化模块-CG优化加载超时，继续保持当前 CG：" +
                    $"controller={_controllerId}, cgId={_latestCgId}, generation={_generation}, " +
                    $"url={_latestDisplayUrl ?? "<null>"}");
            }

            if (_fadeActive)
            {
                AdvanceFade();
            }

            // 快速连续切换时先完成正在进行的淡化，再应用最新已完成资源。
            if (!_fadeActive && _completedPending)
            {
                ApplyCompletedResult();
            }

            if (_holdCommitPending && !_fadeActive && Time.frameCount >= _holdCommitFrame)
            {
                Sprite sprite = _holdCommitSprite;
                CancelHoldCommit();
                CommitHoldImmediately(sprite, "交叉淡化完成后一帧提交");
            }
        }

        private void OnLoadCompleted(
            long generation,
            int cgId,
            string displayUrl,
            string canonicalUrl,
            Sprite loadedSprite)
        {
            if (this == null)
            {
                return;
            }

            if (generation != _generation ||
                !string.Equals(canonicalUrl, _latestCanonicalUrl, StringComparison.Ordinal))
            {
                PatchLog.Warning(
                    "优化模块-CG优化丢弃过期 CG 回调：" +
                    $"controller={_controllerId}, cgId={cgId}, " +
                    $"callbackGeneration={generation}, latestGeneration={_generation}, " +
                    $"url={displayUrl}");
                return;
            }

            _requestPending = false;

            if (loadedSprite == null)
            {
                PatchLog.Error(
                    "优化模块-CG优化资源加载结果为空，继续保持当前 CG：" +
                    $"controller={_controllerId}, cgId={cgId}, generation={generation}, url={displayUrl}");
                return;
            }

            _completedSprite = loadedSprite;
            _completedCgId = cgId;
            _completedDisplayUrl = displayUrl;
            _completedCanonicalUrl = canonicalUrl;
            _completedGeneration = generation;
            _completedPending = true;

            PatchLog.Debug(
                "优化模块-CG优化资源加载完成，等待安全切换：" +
                $"controller={_controllerId}, cgId={cgId}, generation={generation}, " +
                $"sprite={Describe(loadedSprite)}, fadeBusy={_fadeActive}");
        }

        private void ApplyCompletedResult()
        {
            if (!_completedPending)
            {
                return;
            }

            if (_completedGeneration != _generation ||
                !string.Equals(_completedCanonicalUrl, _latestCanonicalUrl, StringComparison.Ordinal))
            {
                PatchLog.Warning(
                    "优化模块-CG优化丢弃待应用的过期 CG：" +
                    $"controller={_controllerId}, cgId={_completedCgId}, " +
                    $"completedGeneration={_completedGeneration}, latestGeneration={_generation}");
                ClearCompletedResult();
                return;
            }

            EnsureLayers();
            EnsureHoldLayer();
            SetLayersActive(true);
            MirrorSourceVisualState();

            Sprite nextSprite = _completedSprite;
            int nextCgId = _completedCgId;
            string nextDisplayUrl = _completedDisplayUrl;
            string nextCanonicalUrl = _completedCanonicalUrl;
            long nextGeneration = _completedGeneration;
            ClearCompletedResult();

            _sourceImage.sprite = nextSprite;
            _sourceImage.enabled = false;

            bool firstDisplay = !_hasCurrent || _frontLayer?.Sprite == null;
            if (firstDisplay)
            {
                _frontLayer.SetSprite(nextSprite);
                _frontLayer.Alpha = 0f;
                _backLayer.Clear();
                _backLayer.Alpha = 0f;
                _hasCurrent = true;
            }
            else
            {
                CommitHoldImmediately(_frontLayer.Sprite, "开始交叉淡化前固定旧CG");
                _frontLayer.Alpha = 1f;
                _backLayer.SetSprite(nextSprite);
                _backLayer.Alpha = 0f;
            }

            _appliedCgId = nextCgId;
            _appliedDisplayUrl = nextDisplayUrl;
            _appliedCanonicalUrl = nextCanonicalUrl;
            _fadeElapsed = 0f;
            _fadeActive = true;

            RestartSlowZoom();
            InvokeEndCallback();

            PatchLog.Debug(
                "优化模块-CG优化开始交叉淡化：" +
                $"controller={_controllerId}, cgId={nextCgId}, generation={nextGeneration}, " +
                $"duration={CrossFadeDuration:F2}, firstDisplay={firstDisplay}, " +
                $"holdVisible={_holdLayer?.HasSprite ?? false}, url={nextDisplayUrl}");
        }

        private void AdvanceFade()
        {
            _fadeElapsed += Time.unscaledDeltaTime;
            float progress = CrossFadeDuration <= 0f
                ? 1f
                : Mathf.Clamp01(_fadeElapsed / CrossFadeDuration);

            bool hasOutgoing = _backLayer?.Sprite != null;
            if (hasOutgoing)
            {
                _frontLayer.Alpha = 1f - progress;
                _backLayer.Alpha = progress;
            }
            else
            {
                _frontLayer.Alpha = progress;
            }

            if (progress < 1f)
            {
                return;
            }

            if (hasOutgoing)
            {
                CgLayer oldFront = _frontLayer;
                _frontLayer = _backLayer;
                _backLayer = oldFront;
                _backLayer.Clear();
                _backLayer.Alpha = 0f;
            }
            else
            {
                _frontLayer.Alpha = 1f;
            }

            _fadeActive = false;
            _fadeElapsed = 0f;
            ScheduleHoldCommit(_frontLayer.Sprite);

            PatchLog.Debug(
                "优化模块-CG优化交叉淡化完成：" +
                $"controller={_controllerId}, cgId={_appliedCgId}, generation={_generation}, " +
                $"current={_appliedDisplayUrl ?? "<null>"}, holdCommitFrame={_holdCommitFrame}");
        }

        private void RestartSlowZoom()
        {
            if (_sourceRect == null)
            {
                return;
            }

            _sourceRect.DOKill(false);

            // 连续 CG 切换时保持当前缩放值，避免重置到 1.0 造成画面瞬间缩小。
            _sourceRect
                .DOScale(SlowZoomTarget, SlowZoomDuration)
                .SetEase(Ease.Linear);
        }

        private void ResolveRequest(string displayUrl, out string canonicalUrl, out bool external)
        {
            external = IsExternalUrl(displayUrl);
            if (external)
            {
                canonicalUrl = ResolveExternalPath(displayUrl);
                return;
            }

            string localizedUrl = LocalizationMgr.GetLocalizeUrl(displayUrl);
            canonicalUrl = Path.Combine("Textures/", localizedUrl ?? displayUrl);
        }

        private static bool IsExternalUrl(string url)
        {
            return !string.IsNullOrEmpty(url) &&
                   (Path.IsPathRooted(url) ||
                    url.StartsWith("Mod", StringComparison.Ordinal));
        }

        private static string ResolveExternalPath(string url)
        {
            if (string.IsNullOrEmpty(url) || Path.IsPathRooted(url))
            {
                return url;
            }

            return Singleton<ModCtrl>.Ins.GetFullUrl(url, null);
        }

        private void EnsureLayers()
        {
            if (_sourceRect == null)
            {
                return;
            }

            if (_layerA == null)
            {
                _layerA = CgLayer.Create(_sourceRect, "CGOptimization_LayerA");
            }

            if (_layerB == null)
            {
                _layerB = CgLayer.Create(_sourceRect, "CGOptimization_LayerB");
            }

            if (_frontLayer == null)
            {
                _frontLayer = _layerA;
                _backLayer = _layerB;
            }
        }

        private void EnsureHoldLayer()
        {
            if (_holdLayer != null || _sourceRect == null || _sourceImage == null)
            {
                return;
            }

            RectTransform rootCg = FindRootCg(_sourceRect);
            RectTransform holdParent = rootCg?.parent as RectTransform;
            if (rootCg == null || holdParent == null)
            {
                PatchLog.Warning(
                    "优化模块-CG优化未找到 root_cg，无法建立独立保底层：" +
                    $"controller={_controllerId}, object={gameObject.name}");
                return;
            }

            GameObject holdObject = new GameObject($"CGOptimization_HoldLayer_{_controllerId}")
            {
                hideFlags = HideFlags.DontSave
            };

            RectTransform holdRect = holdObject.AddComponent<RectTransform>();
            holdRect.SetParent(holdParent, false);
            holdRect.SetSiblingIndex(rootCg.GetSiblingIndex());

            Image holdImage = holdObject.AddComponent<Image>();
            holdImage.raycastTarget = false;

            CanvasGroup holdGroup = holdObject.AddComponent<CanvasGroup>();
            holdGroup.alpha = 0f;
            holdGroup.interactable = false;
            holdGroup.blocksRaycasts = false;

            _holdLayer = holdObject.AddComponent<CGHoldLayer>();
            _holdLayer.Initialize(
                _controllerId,
                rootCg,
                _sourceRect,
                _sourceImage,
                holdRect,
                holdImage,
                holdGroup);

            PatchLog.Debug(
                "优化模块-CG优化独立保底层创建完成：" +
                $"controller={_controllerId}, root={rootCg.name}, parent={holdParent.name}, " +
                $"sibling={holdRect.GetSiblingIndex()}");
        }

        private static RectTransform FindRootCg(RectTransform source)
        {
            Transform current = source;
            RectTransform canvasGroupFallback = null;

            while (current != null)
            {
                if (string.Equals(current.name, "root_cg", StringComparison.Ordinal))
                {
                    return current as RectTransform;
                }

                if (canvasGroupFallback == null &&
                    current != source &&
                    current.GetComponent<CanvasGroup>() != null)
                {
                    canvasGroupFallback = current as RectTransform;
                }

                current = current.parent;
            }

            return canvasGroupFallback;
        }

        private void MirrorSourceVisualState()
        {
            if (_sourceImage == null)
            {
                return;
            }

            _frontLayer?.MirrorFrom(_sourceImage);
            _backLayer?.MirrorFrom(_sourceImage);
            _holdLayer?.MirrorFrom(_sourceImage);
        }

        private void SetLayersActive(bool active)
        {
            _layerA?.SetActive(active);
            _layerB?.SetActive(active);
        }

        private void SetSourceAlpha(float alpha)
        {
            if (_sourceImage == null)
            {
                return;
            }

            Color color = _sourceImage.color;
            color.a = alpha;
            _sourceImage.color = color;
        }

        private void SetUrlValue(string value)
        {
            if (_sprite == null)
            {
                return;
            }

            try
            {
                if (UrlSetter == null)
                {
                    throw new MissingMethodException("未找到 UISprite.url 的私有 setter。");
                }

                UrlSetter.Invoke(_sprite, new object[] { value });
            }
            catch (Exception exception)
            {
                PatchLog.Exception(
                    "优化模块-CG优化写入 UISprite.url 失败：" +
                    $"controller={_controllerId}, value={value ?? "<null>"}",
                    exception);
            }
        }

        private void InvokeEndCallback()
        {
            try
            {
                _sprite.endCallback?.Invoke();
            }
            catch (Exception exception)
            {
                PatchLog.Exception(
                    $"优化模块-CG优化 UISprite.endCallback 执行失败：controller={_controllerId}",
                    exception);
            }
        }

        private void ScheduleHoldCommit(Sprite sprite)
        {
            if (sprite == null)
            {
                return;
            }

            _holdCommitSprite = sprite;
            _holdCommitFrame = Time.frameCount + 1;
            _holdCommitPending = true;
        }

        private void CommitHoldImmediately(Sprite sprite, string reason)
        {
            if (sprite == null)
            {
                return;
            }

            EnsureHoldLayer();
            _holdLayer?.SetSprite(sprite, reason);
        }

        private void CancelHoldCommit()
        {
            _holdCommitPending = false;
            _holdCommitFrame = 0;
            _holdCommitSprite = null;
        }

        private Sprite GetBestCurrentSprite()
        {
            if (_fadeActive && _frontLayer != null && _backLayer != null)
            {
                return _backLayer.Alpha > _frontLayer.Alpha && _backLayer.Sprite != null
                    ? _backLayer.Sprite
                    : _frontLayer.Sprite;
            }

            return _frontLayer?.Sprite ?? _holdLayer?.Sprite;
        }

        private void ClearCompletedResult()
        {
            _completedPending = false;
            _completedSprite = null;
            _completedCgId = -1;
            _completedDisplayUrl = null;
            _completedCanonicalUrl = null;
            _completedGeneration = 0;
        }

        private void Invalidate(string reason)
        {
            _generation++;
            _requestPending = false;
            ClearCompletedResult();
            CancelHoldCommit();
            _fadeActive = false;
            _fadeElapsed = 0f;
            _latestCgId = -1;
            _latestDisplayUrl = null;
            _latestCanonicalUrl = null;

            PatchLog.Debug(
                "优化模块-CG优化请求代次失效：" +
                $"controller={_controllerId}, generation={_generation}, reason={reason}");
        }

        private void ResetLocalVisualState()
        {
            _hasCurrent = false;
            _appliedCgId = -1;
            _appliedDisplayUrl = null;
            _appliedCanonicalUrl = null;

            _frontLayer?.Clear();
            _backLayer?.Clear();
            if (_frontLayer != null) _frontLayer.Alpha = 0f;
            if (_backLayer != null) _backLayer.Alpha = 0f;
            SetLayersActive(false);

            if (_sourceImage != null)
            {
                _sourceImage.sprite = null;
                _sourceImage.enabled = true;
                SetSourceAlpha(1f);
            }

            if (_sourceRect != null)
            {
                _sourceRect.DOKill(false);
                _sourceRect.localScale = Vector3.one;
            }
        }

        private void RegisterController()
        {
            if (!Controllers.Contains(this))
            {
                Controllers.Add(this);
            }
        }

        private void OnDisable()
        {
            if (!_bound)
            {
                return;
            }

            // 连续 CG 调用 CGView.Show 时，icon_cg 可能短暂失活。此处不得清理状态或保底层。
            _holdLayer?.NotifySourceDisabled();
            PatchLog.Debug(
                "优化模块-CG优化检测到CG显示对象暂时隐藏，保留状态等待后续请求：" +
                $"controller={_controllerId}, current={_appliedDisplayUrl ?? "<null>"}");
        }

        private void OnDestroy()
        {
            Controllers.Remove(this);

            if (_bound)
            {
                Invalidate("CG 优化控制器销毁");
            }

            if (_holdLayer != null)
            {
                UnityEngine.Object.Destroy(_holdLayer.gameObject);
                _holdLayer = null;
            }

            PatchLog.Warning(
                $"优化模块-CG优化控制器被销毁：controller={_controllerId}, object={gameObject.name}");
        }

        internal static string Describe(Sprite sprite)
        {
            return sprite == null
                ? "<null>"
                : $"{sprite.name}[{sprite.rect.width:F0}x{sprite.rect.height:F0},id={sprite.GetInstanceID()}]";
        }

        private sealed class CgLayer
        {
            private CgLayer(GameObject gameObject, RectTransform rect, Image image, CanvasGroup group)
            {
                GameObject = gameObject;
                Rect = rect;
                Image = image;
                Group = group;
            }

            internal GameObject GameObject { get; }
            internal RectTransform Rect { get; }
            internal Image Image { get; }
            internal CanvasGroup Group { get; }
            internal Sprite Sprite => Image != null ? Image.sprite : null;

            internal float Alpha
            {
                get => Group != null ? Group.alpha : 0f;
                set
                {
                    if (Group != null)
                    {
                        Group.alpha = Mathf.Clamp01(value);
                    }
                }
            }

            internal static CgLayer Create(RectTransform parent, string name)
            {
                GameObject layerObject = new GameObject(name)
                {
                    hideFlags = HideFlags.DontSave
                };

                RectTransform rect = layerObject.AddComponent<RectTransform>();
                rect.SetParent(parent, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                rect.localScale = Vector3.one;
                rect.localRotation = Quaternion.identity;

                Image image = layerObject.AddComponent<Image>();
                image.raycastTarget = false;

                CanvasGroup group = layerObject.AddComponent<CanvasGroup>();
                group.alpha = 0f;
                group.interactable = false;
                group.blocksRaycasts = false;

                layerObject.SetActive(false);
                return new CgLayer(layerObject, rect, image, group);
            }

            internal void SetSprite(Sprite sprite)
            {
                Image.sprite = sprite;
                SetActive(true);
            }

            internal void Clear()
            {
                if (Image != null)
                {
                    Image.sprite = null;
                }
            }

            internal void SetActive(bool active)
            {
                if (GameObject != null && GameObject.activeSelf != active)
                {
                    GameObject.SetActive(active);
                }
            }

            internal void MirrorFrom(Image source)
            {
                MirrorImageState(Image, source);
            }
        }

        /// <summary>
        /// 位于 root_cg 的同级、且处于 root_cg 正下方的独立层。
        /// 即使 root_cg 或 CGView 在连续 Show 时短暂失活，也能继续遮住正式剧情背景。
        /// </summary>
        internal static void MirrorImageState(Image target, Image source)
        {
            if (target == null || source == null)
            {
                return;
            }

            Color sourceColor = source.color;
            sourceColor.a = 1f;

            if (target.color != sourceColor) target.color = sourceColor;
            if (target.material != source.material) target.material = source.material;
            if (target.type != source.type) target.type = source.type;
            if (target.preserveAspect != source.preserveAspect) target.preserveAspect = source.preserveAspect;
            if (target.fillCenter != source.fillCenter) target.fillCenter = source.fillCenter;
            if (target.fillMethod != source.fillMethod) target.fillMethod = source.fillMethod;
            if (!Mathf.Approximately(target.fillAmount, source.fillAmount)) target.fillAmount = source.fillAmount;
            if (target.fillClockwise != source.fillClockwise) target.fillClockwise = source.fillClockwise;
            if (target.fillOrigin != source.fillOrigin) target.fillOrigin = source.fillOrigin;
            if (target.maskable != source.maskable) target.maskable = source.maskable;
        }
    }
}
