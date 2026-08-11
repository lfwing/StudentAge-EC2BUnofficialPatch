using System;
using System.IO;
using System.Reflection;
using EC2BUnofficialPatch.Core;
using HarmonyLib;
using Sdk;
using UnityEngine;
using UnityEngine.UI;

namespace EC2BUnofficialPatch.Features.Optimization.StaticPortraitOptimization
{
    /// <summary>
    /// 仅服务于 Cell_NewTalkRoleItemUI.icon_role。
    /// 接管异步请求并在 icon_role 自身 CanvasGroup 之下维护两个渲染层。
    /// </summary>
    internal sealed class StaticPortraitTransition : MonoBehaviour
    {
        private const float FadeDuration = 0.15f;
        private const float RequestTimeoutSeconds = 15f;
        private const string RecycleEmptyAtlasUrl = "common6/img_empty";

        private static readonly MethodInfo UrlSetter =
            AccessTools.PropertySetter(typeof(UISprite), nameof(UISprite.url));

        private static int _nextTransitionId;

        private readonly int _transitionId = ++_nextTransitionId;

        private UISprite _sprite;
        private Image _sourceImage;
        private RectTransform _sourceRect;
        private CanvasGroup _roleCanvasGroup;

        private PortraitLayer _layerA;
        private PortraitLayer _layerB;
        private PortraitLayer _frontLayer;
        private PortraitLayer _backLayer;

        private bool _bound;
        private bool _configuredForStatic;
        private bool _proxyActive;
        private bool _recycled = true;
        private bool _allowExpressionCrossfade;
        private bool _requestPending;
        private bool _completedResultPending;
        private bool _fadeActive;

        private int _roleId = -1;
        private int _cloth = -1;
        private int _gradeState = -1;
        private long _generation;

        private string _latestRequestUrl;
        private string _appliedUrl;
        private Sprite _completedSprite;
        private string _completedUrl;
        private long _completedGeneration;
        private bool _completedShouldCrossfade;
        private float _fadeElapsed;
        private float _requestStartedRealtime;

        internal bool IsProxyActive => _proxyActive;

        internal void Bind(UISprite sprite, CanvasGroup roleCanvasGroup)
        {
            if (sprite == null || sprite.image == null || sprite.transform == null)
            {
                throw new ArgumentNullException(nameof(sprite), "UISprite、Image 或 RectTransform 无效。");
            }

            _sprite = sprite;
            _sourceImage = sprite.image;
            _sourceRect = sprite.transform;
            _roleCanvasGroup = roleCanvasGroup;
            _bound = true;

            EnsureLayers();
            SetLayersVisible(false);

            PatchLog.Debug(
                "优化模块-静态立绘过渡组件已绑定：" +
                $"transition={_transitionId}, object={gameObject.name}, " +
                $"canvasGroup={_roleCanvasGroup != null}, sourceSprite={Describe(_sourceImage.sprite)}");
        }

        internal void Configure(bool enabledForStaticPortrait, int roleId, int cloth, int gradeState)
        {
            if (!_bound)
            {
                return;
            }

            bool identityChanged =
                _roleId != roleId ||
                _cloth != cloth ||
                _gradeState != gradeState;

            _roleId = roleId;
            _cloth = cloth;
            _gradeState = gradeState;

            if (!enabledForStaticPortrait)
            {
                LeaveProxyMode("切换为 Live2D 或非静态立绘");
                return;
            }

            _configuredForStatic = true;
            EnterProxyMode();

            if (_recycled || identityChanged)
            {
                InvalidateRequests("人物身份、服装或年级发生变化");
                ClearRenderedLayers(false);
                _appliedUrl = null;
                _allowExpressionCrossfade = false;
            }

            _recycled = false;

            PatchLog.Debug(
                "优化模块-静态立绘上下文配置：" +
                $"transition={_transitionId}, role={roleId}, cloth={cloth}, grade={gradeState}, " +
                $"identityChanged={identityChanged}, generation={_generation}");
        }

        internal void Recycle()
        {
            if (!_bound)
            {
                return;
            }

            _recycled = true;
            _configuredForStatic = false;
            _proxyActive = false;
            _allowExpressionCrossfade = false;
            InvalidateRequests("Cell 回收");
            ClearRenderedLayers(true);
            SetLayersVisible(false);
            _sourceImage.enabled = true;
            SetUrlValue(null);

            PatchLog.Debug(
                $"优化模块-静态立绘 Cell 已回收：transition={_transitionId}, generation={_generation}");
        }

        internal bool TryHandleTextureRequest(string url, bool showWhenComplete)
        {
            if (!_configuredForStatic || !_bound)
            {
                return false;
            }

            _sprite.showWhenComp = showWhenComplete;

            if (url == null)
            {
                ClearFromRequest("SetTextureUrl(null)");
                return true;
            }

            if (IsExternalUrl(url))
            {
                string externalPath = ResolveExternalPath(url);
                BeginExternalRequest(externalPath, false, "SetTextureUrl");
                return true;
            }

            string localizedUrl = LocalizationMgr.GetLocalizeUrl(url);
            string resourceUrl = Path.Combine("Textures/", localizedUrl);
            BeginRequest(
                resourceUrl,
                PortraitRequestKind.Texture,
                callback => ResMgr.LoadSpriteAsync(resourceUrl, callback, null, false),
                "SetTextureUrl");
            return true;
        }

        internal bool TryHandleAtlasRequest(string url, bool showWhenComplete)
        {
            if (!_bound)
            {
                return false;
            }

            if (string.Equals(url, RecycleEmptyAtlasUrl, StringComparison.Ordinal))
            {
                _sprite.showWhenComp = showWhenComplete;
                InvalidateRequests("回收空图请求");
                ClearRenderedLayers(true);
                SetUrlValue(url);
                PatchLog.Debug(
                    $"优化模块-静态立绘已同步处理回收空图：transition={_transitionId}");
                return true;
            }

            if (!_configuredForStatic)
            {
                return false;
            }

            _sprite.showWhenComp = showWhenComplete;

            if (url == null)
            {
                ClearFromRequest("SetAtlasUrl(null)");
                return true;
            }

            if (IsExternalUrl(url))
            {
                string externalPath = ResolveExternalPath(url);
                BeginExternalRequest(externalPath, false, "SetAtlasUrl");
                return true;
            }

            BeginRequest(
                url,
                PortraitRequestKind.Atlas,
                callback => AtlasMgr.GetSpriteAsync(url, callback),
                "SetAtlasUrl");
            return true;
        }

        internal bool TryHandleExternalRequest(string url, bool isReload)
        {
            if (!_configuredForStatic || !_bound)
            {
                return false;
            }

            BeginExternalRequest(url, isReload, "SetExternTextureUrl");
            return true;
        }

        internal void RejectUnexpectedSetSprite(Sprite sprite)
        {
            PatchLog.Warning(
                "优化模块-静态立绘拦截到未登记的 UISprite.SetSprite；" +
                "该调用很可能来自已过期的原版异步回调，已阻止覆盖：" +
                $"transition={_transitionId}, sprite={Describe(sprite)}, " +
                $"latest={_latestRequestUrl ?? "<null>"}, generation={_generation}");
        }

        internal void ClearFromGameCall()
        {
            if (!_bound)
            {
                return;
            }

            ClearFromRequest("UISprite.Clear");
        }

        internal void FallbackToOriginal(string reason)
        {
            if (!_bound)
            {
                return;
            }

            _configuredForStatic = false;
            _proxyActive = false;
            _allowExpressionCrossfade = false;
            InvalidateRequests("发生异常，回退原版：" + reason);
            ClearRenderedLayers(false);
            SetLayersVisible(false);
            _sourceImage.enabled = true;

            PatchLog.Error(
                "优化模块-静态立绘已回退原版显示：" +
                $"transition={_transitionId}, reason={reason}");
        }

        private void BeginExternalRequest(string externalPath, bool isReload, string source)
        {
            if (string.IsNullOrEmpty(externalPath) || !File.Exists(externalPath))
            {
                InvalidateRequests($"外部立绘不存在：{externalPath ?? "<null>"}");
                PatchLog.Error(
                    "优化模块-静态立绘外部资源不存在，保留当前立绘：" +
                    $"transition={_transitionId}, source={source}, path={externalPath ?? "<null>"}");
                return;
            }

            BeginRequest(
                externalPath,
                PortraitRequestKind.External,
                callback => ResMgr.LoadExternSpriteAsync(externalPath, callback, isReload),
                source,
                isReload);
        }

        private void BeginRequest(
            string canonicalUrl,
            PortraitRequestKind kind,
            Action<Action<Sprite>> loader,
            string source,
            bool forceReload = false)
        {
            EnterProxyMode();

            if (!forceReload &&
                string.Equals(canonicalUrl, _latestRequestUrl, StringComparison.Ordinal) &&
                (_requestPending || _completedResultPending))
            {
                EnsureVisibleWhenComplete();
                PatchLog.Debug(
                    "优化模块-静态立绘忽略重复的加载中或待应用请求：" +
                    $"transition={_transitionId}, kind={kind}, url={canonicalUrl}");
                return;
            }

            if (!forceReload &&
                !_requestPending &&
                string.Equals(canonicalUrl, _appliedUrl, StringComparison.Ordinal) &&
                _frontLayer?.Sprite != null)
            {
                _latestRequestUrl = canonicalUrl;
                SetUrlValue(canonicalUrl);
                EnsureVisibleWhenComplete();
                PatchLog.Debug(
                    "优化模块-静态立绘资源已在显示，跳过重复加载：" +
                    $"transition={_transitionId}, kind={kind}, url={canonicalUrl}");
                return;
            }

            long requestGeneration = ++_generation;
            bool shouldCrossfade =
                _allowExpressionCrossfade &&
                _frontLayer?.Sprite != null;

            _allowExpressionCrossfade = true;
            _latestRequestUrl = canonicalUrl;
            _requestPending = true;
            _requestStartedRealtime = Time.realtimeSinceStartup;
            _completedResultPending = false;
            _completedSprite = null;
            SetUrlValue(canonicalUrl);

            PatchLog.Debug(
                "优化模块-静态立绘开始加载：" +
                $"transition={_transitionId}, generation={requestGeneration}, kind={kind}, " +
                $"crossfade={shouldCrossfade}, source={source}, url={canonicalUrl}");

            try
            {
                loader(sprite => OnRequestCompleted(
                    requestGeneration,
                    canonicalUrl,
                    kind,
                    shouldCrossfade,
                    sprite));
            }
            catch (Exception exception)
            {
                if (requestGeneration == _generation)
                {
                    _requestPending = false;
                }

                PatchLog.Exception(
                    "优化模块-静态立绘启动异步加载失败：" +
                    $"transition={_transitionId}, generation={requestGeneration}, url={canonicalUrl}",
                    exception);
            }
        }

        private void OnRequestCompleted(
            long requestGeneration,
            string canonicalUrl,
            PortraitRequestKind kind,
            bool shouldCrossfade,
            Sprite sprite)
        {
            if (this == null || !_bound)
            {
                return;
            }

            if (requestGeneration != _generation ||
                !string.Equals(canonicalUrl, _latestRequestUrl, StringComparison.Ordinal))
            {
                PatchLog.Debug(
                    "优化模块-静态立绘丢弃过期回调：" +
                    $"transition={_transitionId}, callbackGeneration={requestGeneration}, " +
                    $"latestGeneration={_generation}, kind={kind}, url={canonicalUrl}, " +
                    $"sprite={Describe(sprite)}");
                return;
            }

            _requestPending = false;

            if (sprite == null)
            {
                PatchLog.Error(
                    "优化模块-静态立绘加载结果为空，保留当前立绘：" +
                    $"transition={_transitionId}, generation={requestGeneration}, " +
                    $"kind={kind}, url={canonicalUrl}");
                return;
            }

            // 延迟到 LateUpdate 应用，使同一帧后续发出的目标表情请求可以覆盖默认表情请求。
            _completedResultPending = true;
            _completedSprite = sprite;
            _completedUrl = canonicalUrl;
            _completedGeneration = requestGeneration;
            _completedShouldCrossfade = shouldCrossfade;

            PatchLog.Debug(
                "优化模块-静态立绘资源加载完成，等待帧末应用：" +
                $"transition={_transitionId}, generation={requestGeneration}, " +
                $"url={canonicalUrl}, sprite={Describe(sprite)}");
        }

        private void LateUpdate()
        {
            if (!_bound)
            {
                return;
            }

            if (_proxyActive && _sourceImage != null && _sourceImage.enabled)
            {
                _sourceImage.enabled = false;
            }

            MirrorSourceVisualState();
            CheckRequestTimeout();

            if (_completedResultPending)
            {
                ApplyCompletedResult();
            }

            if (_fadeActive)
            {
                AdvanceFade();
            }
        }


        private void CheckRequestTimeout()
        {
            if (!_requestPending || Time.realtimeSinceStartup - _requestStartedRealtime < RequestTimeoutSeconds)
            {
                return;
            }

            string timedOutUrl = _latestRequestUrl;
            long timedOutGeneration = _generation;
            _generation++;
            _requestPending = false;
            _completedResultPending = false;
            _completedSprite = null;
            _completedUrl = null;
            _latestRequestUrl = null;

            PatchLog.Error(
                "优化模块-静态立绘加载超时，已使该请求失效并保留当前立绘：" +
                $"transition={_transitionId}, generation={timedOutGeneration}, " +
                $"timeout={RequestTimeoutSeconds:F0}s, url={timedOutUrl ?? "<null>"}");
        }

        private void ApplyCompletedResult()
        {
            Sprite sprite = _completedSprite;
            string url = _completedUrl;
            long requestGeneration = _completedGeneration;
            bool shouldCrossfade = _completedShouldCrossfade;

            _completedResultPending = false;
            _completedSprite = null;
            _completedUrl = null;

            if (requestGeneration != _generation ||
                !string.Equals(url, _latestRequestUrl, StringComparison.Ordinal))
            {
                PatchLog.Debug(
                    "优化模块-静态立绘帧末应用时发现结果已过期，跳过：" +
                    $"transition={_transitionId}, callbackGeneration={requestGeneration}, " +
                    $"latestGeneration={_generation}, url={url}");
                return;
            }

            if (!_configuredForStatic || !_proxyActive || sprite == null)
            {
                return;
            }

            EnsureLayers();
            CollapseActiveFadeToDominantLayer();

            if (_frontLayer?.Sprite == sprite)
            {
                ApplySourceSpriteMetadata(sprite);
                _appliedUrl = url;
                EnsureVisibleWhenComplete();
                InvokeEndCallback();
                return;
            }

            Vector2 oldSize = _sourceRect.rect.size;
            ApplySourceSpriteMetadata(sprite);
            Vector2 newSize = _sourceRect.rect.size;

            if (_frontLayer.Sprite == null || !shouldCrossfade)
            {
                _frontLayer.SetSprite(sprite, newSize);
                _frontLayer.Alpha = 1f;
                _backLayer.Clear();
                _backLayer.Alpha = 0f;
                _fadeActive = false;

                PatchLog.Debug(
                    "优化模块-静态立绘立即应用：" +
                    $"transition={_transitionId}, generation={requestGeneration}, " +
                    $"url={url}, sprite={Describe(sprite)}, size={FormatSize(newSize)}");
            }
            else
            {
                // frontLayer 保留旧图及其旧尺寸；backLayer 使用新图和新尺寸。
                // oldSize 仅用于日志，旧层尺寸由它自身在上一次应用时保存。
                _backLayer.SetSprite(sprite, newSize);
                _backLayer.Alpha = 0f;
                _backLayer.Rect.SetAsLastSibling();
                _frontLayer.Alpha = 1f;
                _fadeElapsed = 0f;
                _fadeActive = true;

                PatchLog.Debug(
                    "优化模块-静态立绘交叉淡化开始：" +
                    $"transition={_transitionId}, generation={requestGeneration}, " +
                    $"old={Describe(_frontLayer.Sprite)}, new={Describe(sprite)}, " +
                    $"oldSize={FormatSize(oldSize)}, newSize={FormatSize(newSize)}, " +
                    $"duration={FadeDuration:F2}s");
            }

            _appliedUrl = url;
            EnsureVisibleWhenComplete();
            InvokeEndCallback();
        }

        private void AdvanceFade()
        {
            if (_frontLayer == null || _backLayer == null)
            {
                _fadeActive = false;
                return;
            }

            _fadeElapsed += Time.unscaledDeltaTime;
            float progress = FadeDuration <= 0f
                ? 1f
                : Mathf.Clamp01(_fadeElapsed / FadeDuration);

            // 线性交叉淡化使两层总透明度保持稳定，避免 SmoothStep 造成中段亮度变化。
            _frontLayer.Alpha = 1f - progress;
            _backLayer.Alpha = progress;

            if (progress < 1f)
            {
                return;
            }

            PortraitLayer oldFront = _frontLayer;
            _frontLayer = _backLayer;
            _backLayer = oldFront;

            _frontLayer.Alpha = 1f;
            _backLayer.Clear();
            _backLayer.Alpha = 0f;
            _fadeActive = false;

            PatchLog.Debug(
                "优化模块-静态立绘交叉淡化完成：" +
                $"transition={_transitionId}, sprite={Describe(_frontLayer.Sprite)}");
        }

        private void CollapseActiveFadeToDominantLayer()
        {
            if (!_fadeActive || _frontLayer == null || _backLayer == null)
            {
                return;
            }

            PortraitLayer dominant =
                _backLayer.Alpha > _frontLayer.Alpha
                    ? _backLayer
                    : _frontLayer;
            PortraitLayer discarded =
                ReferenceEquals(dominant, _frontLayer)
                    ? _backLayer
                    : _frontLayer;

            _frontLayer = dominant;
            _backLayer = discarded;
            _frontLayer.Alpha = 1f;
            _backLayer.Clear();
            _backLayer.Alpha = 0f;
            _fadeActive = false;

            PatchLog.Debug(
                "优化模块-静态立绘快速连续切换，保留当前占优图层：" +
                $"transition={_transitionId}, sprite={Describe(_frontLayer.Sprite)}");
        }

        private void ApplySourceSpriteMetadata(Sprite sprite)
        {
            _sourceImage.sprite = sprite;
            if (_sprite.sizeType == UISpriteSizeType.NativeSize)
            {
                _sourceImage.SetNativeSize();
            }
        }

        private void EnterProxyMode()
        {
            if (!_bound)
            {
                return;
            }

            EnsureLayers();

            if (!_proxyActive)
            {
                _proxyActive = true;

                if (_sourceImage.sprite != null && _frontLayer.Sprite == null)
                {
                    _frontLayer.SetSprite(_sourceImage.sprite, _sourceRect.rect.size);
                    _frontLayer.Alpha = 1f;
                    _appliedUrl = _sprite.url;
                }

                _sourceImage.enabled = false;
                SetLayersVisible(true);
            }
        }

        private void LeaveProxyMode(string reason)
        {
            if (!_bound)
            {
                return;
            }

            _configuredForStatic = false;
            _proxyActive = false;
            _allowExpressionCrossfade = false;
            InvalidateRequests(reason);
            ClearRenderedLayers(false);
            SetLayersVisible(false);
            _sourceImage.enabled = true;

            PatchLog.Debug(
                $"优化模块-静态立绘退出代理模式：transition={_transitionId}, reason={reason}");
        }

        private void ClearFromRequest(string reason)
        {
            InvalidateRequests(reason);
            ClearRenderedLayers(true);
            SetUrlValue(null);
            _appliedUrl = null;

            PatchLog.Debug(
                $"优化模块-静态立绘已清空：transition={_transitionId}, reason={reason}");
        }

        private void ClearRenderedLayers(bool clearSourceSprite)
        {
            _fadeActive = false;
            _fadeElapsed = 0f;

            _frontLayer?.Clear();
            _backLayer?.Clear();

            if (_frontLayer != null)
            {
                _frontLayer.Alpha = 0f;
            }

            if (_backLayer != null)
            {
                _backLayer.Alpha = 0f;
            }

            if (clearSourceSprite && _sourceImage != null)
            {
                _sourceImage.sprite = null;
            }
        }

        private void InvalidateRequests(string reason)
        {
            _generation++;
            _requestPending = false;
            _completedResultPending = false;
            _completedSprite = null;
            _completedUrl = null;
            _latestRequestUrl = null;

            PatchLog.Debug(
                "优化模块-静态立绘请求代次失效：" +
                $"transition={_transitionId}, generation={_generation}, reason={reason}");
        }

        private void EnsureLayers()
        {
            if (_sourceRect == null)
            {
                return;
            }

            if (_layerA == null)
            {
                _layerA = PortraitLayer.Create(_sourceRect, "StaticPortrait_LayerA");
            }

            if (_layerB == null)
            {
                _layerB = PortraitLayer.Create(_sourceRect, "StaticPortrait_LayerB");
            }

            if (_frontLayer == null)
            {
                _frontLayer = _layerA;
                _backLayer = _layerB;
            }
        }

        private void MirrorSourceVisualState()
        {
            if (_sourceImage == null)
            {
                return;
            }

            _frontLayer?.MirrorFrom(_sourceImage, _sourceRect);
            _backLayer?.MirrorFrom(_sourceImage, _sourceRect);
        }

        private void SetLayersVisible(bool visible)
        {
            _layerA?.SetActive(visible);
            _layerB?.SetActive(visible);
        }

        private void EnsureVisibleWhenComplete()
        {
            if (_sprite.showWhenComp && _sourceImage?.gameObject != null)
            {
                _sourceImage.gameObject.SetActive(true);
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
                    $"优化模块-静态立绘 endCallback 执行失败：transition={_transitionId}",
                    exception);
            }
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
                    "优化模块-静态立绘写入 UISprite.url 失败：" +
                    $"transition={_transitionId}, value={value ?? "<null>"}",
                    exception);
            }
        }

        private static bool IsExternalUrl(string url)
        {
            return !string.IsNullOrEmpty(url) &&
                   (Path.IsPathRooted(url) || url.StartsWith("Mods", StringComparison.Ordinal));
        }

        private static string ResolveExternalPath(string url)
        {
            if (string.IsNullOrEmpty(url) || Path.IsPathRooted(url))
            {
                return url;
            }

            return Singleton<ModCtrl>.Ins.GetFullUrl(url, null);
        }

        private void OnDestroy()
        {
            InvalidateRequests("过渡组件销毁");
            PatchLog.Warning(
                "优化模块-静态立绘过渡组件被销毁：" +
                $"transition={_transitionId}, role={_roleId}, cloth={_cloth}, grade={_gradeState}");
        }

        private static string Describe(Sprite sprite)
        {
            return sprite == null
                ? "<null>"
                : $"{sprite.name}[{sprite.rect.width:F0}x{sprite.rect.height:F0},id={sprite.GetInstanceID()}]";
        }

        private static string FormatSize(Vector2 size)
        {
            return $"{size.x:F0}x{size.y:F0}";
        }

        private enum PortraitRequestKind
        {
            Texture,
            Atlas,
            External
        }

        private sealed class PortraitLayer
        {
            private PortraitLayer(GameObject gameObject, RectTransform rect, Image image, CanvasGroup group)
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

            internal static PortraitLayer Create(RectTransform parent, string name)
            {
                GameObject layerObject = new GameObject(name)
                {
                    hideFlags = HideFlags.DontSave
                };

                RectTransform rect = layerObject.AddComponent<RectTransform>();
                rect.SetParent(parent, false);

                Image image = layerObject.AddComponent<Image>();
                image.raycastTarget = false;

                CanvasGroup group = layerObject.AddComponent<CanvasGroup>();
                group.alpha = 0f;
                group.interactable = false;
                group.blocksRaycasts = false;

                PortraitLayer layer = new PortraitLayer(layerObject, rect, image, group);
                layer.AlignToParent(parent);
                layerObject.SetActive(false);
                return layer;
            }

            internal void SetSprite(Sprite sprite, Vector2 size)
            {
                Image.sprite = sprite;
                Rect.sizeDelta = size;
                GameObject.SetActive(true);
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

            internal void MirrorFrom(Image source, RectTransform sourceRect)
            {
                if (Image == null || source == null || Rect == null || sourceRect == null)
                {
                    return;
                }

                if (Image.color != source.color) Image.color = source.color;
                if (Image.material != source.material) Image.material = source.material;
                if (Image.type != source.type) Image.type = source.type;
                if (Image.preserveAspect != source.preserveAspect) Image.preserveAspect = source.preserveAspect;
                if (Image.fillCenter != source.fillCenter) Image.fillCenter = source.fillCenter;
                if (Image.fillMethod != source.fillMethod) Image.fillMethod = source.fillMethod;
                if (!Mathf.Approximately(Image.fillAmount, source.fillAmount)) Image.fillAmount = source.fillAmount;
                if (Image.fillClockwise != source.fillClockwise) Image.fillClockwise = source.fillClockwise;
                if (Image.fillOrigin != source.fillOrigin) Image.fillOrigin = source.fillOrigin;
                if (Image.maskable != source.maskable) Image.maskable = source.maskable;

                AlignToParent(sourceRect);
            }

            private void AlignToParent(RectTransform parent)
            {
                Vector2 pivot = parent.pivot;
                if (Rect.anchorMin != pivot) Rect.anchorMin = pivot;
                if (Rect.anchorMax != pivot) Rect.anchorMax = pivot;
                if (Rect.pivot != pivot) Rect.pivot = pivot;
                if (Rect.anchoredPosition != Vector2.zero) Rect.anchoredPosition = Vector2.zero;
                if (Rect.localScale != Vector3.one) Rect.localScale = Vector3.one;
                if (Rect.localRotation != Quaternion.identity) Rect.localRotation = Quaternion.identity;
            }
        }
    }
}
