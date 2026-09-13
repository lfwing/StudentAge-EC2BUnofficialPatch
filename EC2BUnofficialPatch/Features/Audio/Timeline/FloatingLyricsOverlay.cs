using System;
using System.Collections.Generic;
using GenUI.Talk;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace LFBetterAudio.Timeline
{
    public sealed class FloatingLyricsOverlay
    {
        private const float TargetY = -36f;
        private const float StartOffsetY = -14f;
        private const float FadeSpeed = 7.5f;
        private const float MoveSpeed = 9f;
        private const float BaseFontSize = 38f;
        private const float MaximumContentWidth = 1840f;
        private const float MaximumPrefixWidth = 620f;
        private const float MinimumFrameWidth = 520f;
        private const float HorizontalPadding = 24f;
        private const float VerticalPadding = 12f;
        private const float ToolbarHeight = 38f;
        private const float EdgePadding = 10f;
        private const float FrameAutoHideSeconds = 4.5f;
        private const float DragThresholdPixels = 4f;
        private const float DarkToLightOutlineThreshold = 0.40f;
        private const float LightToDarkOutlineThreshold = 0.60f;

        private const float SecondaryMenuWidth = 118f;
        private const float SecondaryMenuHeight = 76f;
        private const float SizeMenuWidth = 132f;
        private const float SizeMenuHeight = 178f;
        private const float ColorMenuWidth = 226f;
        private const float ColorMenuHeight = 328f;

        private static readonly Color DarkOutlineColor = new Color(0f, 0f, 0f, 0.82f);
        private static readonly Color LightOutlineColor = new Color(1f, 1f, 1f, 0.78f);
        private static readonly Color HiddenFrameColor = new Color(0.24f, 0.24f, 0.24f, 0f);
        private static readonly Color VisibleFrameColor = new Color(0.24f, 0.24f, 0.24f, 0.58f);
        private static readonly Color ButtonColor = new Color(0.12f, 0.12f, 0.12f, 0.58f);
        private static readonly Color ButtonHoverColor = new Color(0.32f, 0.32f, 0.32f, 0.86f);
        private static readonly Color MenuPanelColor = new Color(0.16f, 0.16f, 0.16f, 0.92f);
        private static readonly Color MenuItemColor = new Color(0.25f, 0.25f, 0.25f, 0.72f);
        private static readonly Color MenuItemSelectedColor = new Color(0.48f, 0.48f, 0.48f, 0.94f);

        // 不再依赖 TMP 字体中的 Unicode 齿轮/回退字形。部分游戏字体不含这些字形，
        // 会只剩按钮的灰色底框。图标改为运行时生成的透明 Sprite。
        private static Sprite _settingsIconSprite;
        private static Sprite _restoreIconSprite;
        private static Sprite _openLockIconSprite;
        private static Sprite _closedLockIconSprite;

        private readonly Vector3[] _worldCorners = new Vector3[4];
        private readonly List<Image> _sizeOptionImages = new List<Image>();
        private readonly List<Image> _colorOptionImages = new List<Image>();

        private GameObject _root;
        private RectTransform _rect;
        private RectTransform _parentRect;
        private RectTransform _contentRoot;
        private CanvasGroup _canvasGroup;
        private Image _frameBackground;
        private GameObject _toolbarRoot;
        private Image _lockIconImage;
        private GameObject _secondaryMenuObject;
        private RectTransform _secondaryMenuRect;
        private GameObject _tertiaryMenuObject;
        private RectTransform _tertiaryMenuRect;
        private TextMeshProUGUI _prefixText;
        private TextMeshProUGUI _lyricText;
        private RectTransform _prefixRect;
        private RectTransform _lyricRect;
        private LyricsContrastProbe _contrastProbe;
        private FloatingLyricsRuntimeState _runtimeState;
        private TMP_FontAsset _ownerFont;

        private string _currentKey;
        private string _currentPrefix = string.Empty;
        private string _currentLyricBlock = string.Empty;
        private bool _currentHasSecondLine;
        private int _currentLineColorMode = -1;
        private float _fontSize = BaseFontSize;
        private float _fontScale = 1f;
        private Color _baseColor = Color.white;
        private bool _usingDarkOutline = true;
        private bool _frameVisible;
        private bool _secondaryMenuVisible;
        private float _frameAutoHideAt;

        private bool _isDragging;
        private bool _dragMoved;
        private Vector2 _dragStartPointerLocal;
        private Vector2 _dragStartAnchoredPosition;
        private Vector2 _pointerDownScreenPosition;

        public bool IsAlive => _root != null;

        internal bool CanSampleContrast =>
            IsAlive &&
            !_frameVisible &&
            !_isDragging &&
            _canvasGroup != null &&
            _canvasGroup.alpha > 0.05f &&
            !string.IsNullOrEmpty(_currentKey);

        public void Attach(
            NewTalkUI owner,
            FloatingLyricsRuntimeState runtimeState)
        {
            Destroy();
            if (owner == null || owner.group_top == null || runtimeState == null)
            {
                return;
            }

            _runtimeState = runtimeState;
            _usingDarkOutline = true;
            _ownerFont = owner.txtex_content != null
                ? owner.txtex_content.font
                : null;

            _root = new GameObject(
                "BetterAudioFloatingLyrics",
                typeof(RectTransform),
                typeof(LyricsContrastProbe));
            _root.layer = owner.group_top.gameObject.layer;
            _root.transform.SetParent(owner.group_top, false);
            _root.transform.SetAsLastSibling();

            _parentRect = owner.group_top.GetComponent<RectTransform>();
            _rect = _root.GetComponent<RectTransform>();
            _rect.anchorMin = new Vector2(0.5f, 1f);
            _rect.anchorMax = new Vector2(0.5f, 1f);
            _rect.pivot = new Vector2(0.5f, 1f);
            _rect.sizeDelta = new Vector2(MinimumFrameWidth, 110f);
            _rect.anchoredPosition = runtimeState.TryGetPreferredPosition(out Vector2 preferredPosition)
                ? preferredPosition
                : new Vector2(0f, TargetY);

            CreateFrameSurface();
            CreateLyricsContent();
            CreateToolbar();

            _contrastProbe = _root.GetComponent<LyricsContrastProbe>();
            _contrastProbe?.Configure(this, _rect);

            ApplyStyle(runtimeState.EffectiveSizeMode);
            Clear();
            RefreshLockIcon();
            ClampFrameToParent(runtimeState.HasCustomPosition || runtimeState.IsPositionLocked);
        }

        public void ApplyStyle(int sizeMode, int colorMode)
        {
            // 兼容旧调用入口；运行时状态存在时仍以状态中的有效值为准。
            if (_runtimeState != null)
            {
                ApplyStyle(_runtimeState.EffectiveSizeMode);
                ApplyCurrentLineColor();
                RefreshCurrentLayoutAndClamp();
                return;
            }

            ApplyStyle(sizeMode);
            _baseColor = ResolveColor(colorMode);
            _baseColor.a = 0.98f;
            ApplyCurrentLineColor();
            RefreshCurrentLayoutAndClamp();
        }

        public void ShowLine(
            LrcLine line,
            string singerName,
            int colorMode,
            bool animate = true)
        {
            if (line == null)
            {
                Clear();
                return;
            }

            ShowLine(
                line.PrimaryText,
                line.SecondaryText,
                singerName,
                colorMode,
                animate);
        }

        public void ShowLine(
            string primaryText,
            string secondaryText,
            string singerName,
            int colorMode,
            bool animate = true)
        {
            if (!IsAlive || _prefixText == null || _lyricText == null)
            {
                return;
            }

            primaryText = primaryText ?? string.Empty;
            secondaryText = secondaryText ?? string.Empty;
            singerName = singerName ?? string.Empty;
            string prefix = string.IsNullOrWhiteSpace(singerName)
                ? string.Empty
                : singerName.Trim() + "：";

            string key = prefix + "\u001f" + primaryText + "\u001f" + secondaryText + "\u001f" + colorMode;
            if (key == _currentKey)
            {
                if (!animate && _canvasGroup != null && _contentRoot != null)
                {
                    _canvasGroup.alpha = 1f;
                    _contentRoot.anchoredPosition = Vector2.zero;
                }
                return;
            }

            _currentKey = key;
            _currentPrefix = prefix;
            _currentLyricBlock = string.IsNullOrEmpty(secondaryText)
                ? primaryText
                : primaryText + "\n" + secondaryText;
            _currentHasSecondLine = !string.IsNullOrEmpty(secondaryText);
            _currentLineColorMode = colorMode;

            _prefixText.text = _currentPrefix;
            _lyricText.text = _currentLyricBlock;
            if (_frameBackground != null)
            {
                _frameBackground.raycastTarget = true;
            }

            ApplyCurrentLineColor();
            RefreshLayout(
                _currentPrefix,
                _currentLyricBlock,
                _currentHasSecondLine);

            _canvasGroup.alpha = animate ? 0f : 1f;
            _contentRoot.anchoredPosition = animate
                ? new Vector2(0f, StartOffsetY)
                : Vector2.zero;
            bool persistPosition = _runtimeState != null &&
                (_runtimeState.HasCustomPosition || _runtimeState.IsPositionLocked);
            ClampFrameToParent(persistPosition);

            if (_runtimeState != null && _runtimeState.IsPositionLocked)
            {
                ShowSelectionFrame();
            }
        }

        public void ShowLine(string text)
        {
            ShowLine(text, string.Empty, string.Empty, -1, true);
        }

        public void Clear()
        {
            _currentKey = string.Empty;
            _currentPrefix = string.Empty;
            _currentLyricBlock = string.Empty;
            _currentHasSecondLine = false;
            _currentLineColorMode = -1;

            if (_prefixText != null)
            {
                _prefixText.text = string.Empty;
            }

            if (_lyricText != null)
            {
                _lyricText.text = string.Empty;
            }

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
            }

            if (_frameBackground != null)
            {
                _frameBackground.raycastTarget = false;
            }

            HideSelectionFrame(true);
        }

        public void Tick(float unscaledDeltaTime)
        {
            if (!IsAlive)
            {
                return;
            }

            // 二级/三级菜单不会锁住选框。只要玩家在设定时间内没有新的点击、
            // 拖动或进入菜单项，灰框、工具栏和全部菜单会一起隐藏。
            if (_frameVisible && !_isDragging &&
                !IsPositionLocked &&
                Time.unscaledTime >= _frameAutoHideAt)
            {
                HideSelectionFrame();
            }

            if (_canvasGroup == null || _contentRoot == null ||
                string.IsNullOrEmpty(_currentKey))
            {
                return;
            }

            _canvasGroup.alpha = Mathf.MoveTowards(
                _canvasGroup.alpha,
                1f,
                FadeSpeed * unscaledDeltaTime);

            Vector2 position = _contentRoot.anchoredPosition;
            position.y = Mathf.Lerp(
                position.y,
                0f,
                1f - Mathf.Exp(-MoveSpeed * unscaledDeltaTime));
            _contentRoot.anchoredPosition = position;
        }

        public void Destroy()
        {
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
            }

            _root = null;
            _rect = null;
            _parentRect = null;
            _contentRoot = null;
            _canvasGroup = null;
            _frameBackground = null;
            _toolbarRoot = null;
            _lockIconImage = null;
            _secondaryMenuObject = null;
            _secondaryMenuRect = null;
            _tertiaryMenuObject = null;
            _tertiaryMenuRect = null;
            _prefixText = null;
            _lyricText = null;
            _prefixRect = null;
            _lyricRect = null;
            _contrastProbe = null;
            _runtimeState = null;
            _ownerFont = null;
            _currentKey = null;
            _sizeOptionImages.Clear();
            _colorOptionImages.Clear();
            _frameVisible = false;
            _secondaryMenuVisible = false;
            _frameAutoHideAt = 0f;
            _isDragging = false;
        }

        internal void ApplyBackgroundLuminance(float luminance)
        {
            bool nextDarkOutline = _usingDarkOutline;
            if (_usingDarkOutline && luminance < DarkToLightOutlineThreshold)
            {
                nextDarkOutline = false;
            }
            else if (!_usingDarkOutline && luminance > LightToDarkOutlineThreshold)
            {
                nextDarkOutline = true;
            }

            if (nextDarkOutline == _usingDarkOutline)
            {
                return;
            }

            _usingDarkOutline = nextDarkOutline;
            ApplyOutlineAndShadow();
        }

        private void CreateFrameSurface()
        {
            GameObject surface = CreateUiObject(
                "LyricsHitSurface",
                _root.transform,
                typeof(Image),
                typeof(EventTrigger));
            RectTransform surfaceRect = surface.GetComponent<RectTransform>();
            StretchToParent(surfaceRect);

            _frameBackground = surface.GetComponent<Image>();
            _frameBackground.color = HiddenFrameColor;
            _frameBackground.raycastTarget = true;

            AddTrigger(surface, EventTriggerType.PointerDown, data =>
            {
                PointerEventData pointer = data as PointerEventData;
                if (pointer == null || pointer.button != PointerEventData.InputButton.Left)
                {
                    return;
                }

                _pointerDownScreenPosition = pointer.position;
                _dragMoved = false;
                TouchInteraction();
            });
            AddTrigger(surface, EventTriggerType.PointerClick, data =>
            {
                PointerEventData pointer = data as PointerEventData;
                if (pointer == null || pointer.button != PointerEventData.InputButton.Left)
                {
                    return;
                }

                if (!_dragMoved &&
                    Vector2.Distance(_pointerDownScreenPosition, pointer.position) <= DragThresholdPixels)
                {
                    ShowSelectionFrame();
                }

                TouchInteraction();
            });
            AddTrigger(surface, EventTriggerType.BeginDrag, BeginDrag);
            AddTrigger(surface, EventTriggerType.Drag, Drag);
            AddTrigger(surface, EventTriggerType.EndDrag, EndDrag);
            AddTrigger(surface, EventTriggerType.PointerEnter, _ => TouchInteraction());
        }

        private void CreateLyricsContent()
        {
            GameObject content = CreateUiObject(
                "LyricContent",
                _root.transform,
                typeof(CanvasGroup));
            _contentRoot = content.GetComponent<RectTransform>();
            StretchToParent(_contentRoot);

            _canvasGroup = content.GetComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;

            _prefixText = CreateText(
                "SingerPrefix",
                content.transform,
                out _prefixRect);
            _lyricText = CreateText(
                "LyricBlock",
                content.transform,
                out _lyricRect);
        }

        private void CreateToolbar()
        {
            _toolbarRoot = CreateUiObject(
                "LyricsToolbar",
                _root.transform);
            RectTransform toolbarRect = _toolbarRoot.GetComponent<RectTransform>();
            toolbarRect.anchorMin = new Vector2(0.5f, 1f);
            toolbarRect.anchorMax = new Vector2(0.5f, 1f);
            toolbarRect.pivot = new Vector2(0.5f, 1f);
            toolbarRect.sizeDelta = new Vector2(114f, 34f);
            toolbarRect.anchoredPosition = new Vector2(0f, -2f);

            EnsureToolbarIconSprites();
            CreateToolbarButton(
                "Settings",
                toolbarRect,
                new Vector2(-38f, -17f),
                _settingsIconSprite,
                ToggleSecondaryMenu);
            CreateToolbarButton(
                "Restore",
                toolbarRect,
                new Vector2(0f, -17f),
                _restoreIconSprite,
                ResetRuntimeStyle);
            _lockIconImage = CreateToolbarButton(
                "PositionLock",
                toolbarRect,
                new Vector2(38f, -17f),
                _openLockIconSprite,
                TogglePositionLock);
            RefreshLockIcon();

            _toolbarRoot.SetActive(false);
        }

        private Image CreateToolbarButton(
            string name,
            RectTransform parent,
            Vector2 anchoredPosition,
            Sprite iconSprite,
            Action action)
        {
            GameObject button = CreateUiObject(
                name,
                parent,
                typeof(Image),
                typeof(EventTrigger));
            RectTransform rect = button.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(32f, 32f);
            rect.anchoredPosition = anchoredPosition;

            Image image = button.GetComponent<Image>();
            image.color = ButtonColor;
            image.raycastTarget = true;

            Image icon = CreateToolbarIcon(button.transform, iconSprite);
            AddInteractiveVisualTriggers(button, image, ButtonColor, ButtonHoverColor);
            AddTrigger(button, EventTriggerType.PointerClick, data =>
            {
                PointerEventData pointer = data as PointerEventData;
                if (pointer == null || pointer.button != PointerEventData.InputButton.Left)
                {
                    return;
                }

                TouchInteraction();
                action?.Invoke();
            });
            return icon;
        }

        private bool IsPositionLocked =>
            _runtimeState != null && _runtimeState.IsPositionLocked;

        private void TogglePositionLock()
        {
            if (_runtimeState == null || _rect == null)
            {
                return;
            }

            if (IsPositionLocked)
            {
                _runtimeState.UnlockPosition();
                RefreshLockIcon();
                _frameAutoHideAt = Time.unscaledTime + FrameAutoHideSeconds;
                TouchInteraction();
                return;
            }

            ClampFrameToParent(false);
            _runtimeState.LockPosition(_rect.anchoredPosition);
            RefreshLockIcon();
            ShowSelectionFrame();
            HideMenus();
        }

        private void RefreshLockIcon()
        {
            if (_lockIconImage == null)
            {
                return;
            }

            _lockIconImage.sprite = IsPositionLocked
                ? _closedLockIconSprite
                : _openLockIconSprite;
        }

        private void ToggleSecondaryMenu()
        {
            ShowSelectionFrame();
            if (_secondaryMenuVisible)
            {
                HideMenus();
                return;
            }

            EnsureSecondaryMenu();
            TouchInteraction();
            _secondaryMenuVisible = true;
            _secondaryMenuObject.SetActive(true);
            ClampMenuToParent(_secondaryMenuRect);
        }

        private void EnsureSecondaryMenu()
        {
            if (_secondaryMenuObject != null)
            {
                return;
            }

            _secondaryMenuObject = CreateUiObject(
                "LyricsSettingsMenu",
                _root.transform,
                typeof(Image),
                typeof(EventTrigger));
            _secondaryMenuRect = _secondaryMenuObject.GetComponent<RectTransform>();
            _secondaryMenuRect.anchorMin = new Vector2(0.5f, 1f);
            _secondaryMenuRect.anchorMax = new Vector2(0.5f, 1f);
            _secondaryMenuRect.pivot = new Vector2(0.5f, 1f);
            _secondaryMenuRect.sizeDelta = new Vector2(
                SecondaryMenuWidth,
                SecondaryMenuHeight);
            _secondaryMenuRect.anchoredPosition = new Vector2(0f, -ToolbarHeight);

            Image panel = _secondaryMenuObject.GetComponent<Image>();
            panel.color = MenuPanelColor;
            panel.raycastTarget = true;
            AddTrigger(_secondaryMenuObject, EventTriggerType.PointerEnter, _ => TouchInteraction());

            CreateSecondaryMenuItem("FontSize", "字号", 0, ShowSizeMenu);
            CreateSecondaryMenuItem("Color", "颜色", 1, ShowColorMenu);
            _secondaryMenuObject.SetActive(false);
        }

        private void CreateSecondaryMenuItem(
            string name,
            string label,
            int row,
            Action showTertiary)
        {
            GameObject item = CreateUiObject(
                name,
                _secondaryMenuObject.transform,
                typeof(Image),
                typeof(EventTrigger));
            RectTransform rect = item.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(SecondaryMenuWidth - 8f, 32f);
            rect.anchoredPosition = new Vector2(0f, -5f - row * 34f);

            Image image = item.GetComponent<Image>();
            image.color = MenuItemColor;
            image.raycastTarget = true;
            CreateLabel(item.transform, label, 20f, TextAlignmentOptions.Center, true);
            AddInteractiveVisualTriggers(item, image, MenuItemColor, ButtonHoverColor);
            AddTrigger(item, EventTriggerType.PointerEnter, _ =>
            {
                TouchInteraction();
                showTertiary?.Invoke();
            });
            AddTrigger(item, EventTriggerType.PointerClick, data =>
            {
                PointerEventData pointer = data as PointerEventData;
                if (pointer != null && pointer.button == PointerEventData.InputButton.Left)
                {
                    TouchInteraction();
                    showTertiary?.Invoke();
                }
            });
        }

        private void ShowSizeMenu()
        {
            EnsureTertiaryMenu(SizeMenuWidth, SizeMenuHeight, "LyricsSizeMenu");
            _sizeOptionImages.Clear();
            _colorOptionImages.Clear();
            DestroyChildrenExceptPanel(_tertiaryMenuRect);

            string[] labels = { "0.8×", "1.0×", "1.2×", "1.5×", "1.8×" };
            float itemWidth = SizeMenuWidth - 10f;
            float itemHeight = 30f;
            for (int i = 0; i < labels.Length; i++)
            {
                int captured = i;
                GameObject item = CreateUiObject(
                    "Size" + captured,
                    _tertiaryMenuRect,
                    typeof(Image),
                    typeof(EventTrigger));
                RectTransform rect = item.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 1f);
                rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(itemWidth, itemHeight);
                rect.anchoredPosition = new Vector2(0f, -6f - i * 33f);

                Image image = item.GetComponent<Image>();
                image.raycastTarget = true;
                _sizeOptionImages.Add(image);
                CreateLabel(item.transform, labels[i], 18f, TextAlignmentOptions.Center, true);
                AddTrigger(item, EventTriggerType.PointerEnter, _ => TouchInteraction());
                AddTrigger(item, EventTriggerType.PointerClick, data =>
                {
                    PointerEventData pointer = data as PointerEventData;
                    if (pointer == null || pointer.button != PointerEventData.InputButton.Left)
                    {
                        return;
                    }

                    SetRuntimeSize(captured);
                });
            }

            RefreshMenuSelectionVisuals();
            PositionAndShowTertiaryMenu();
        }

        private void ShowColorMenu()
        {
            EnsureTertiaryMenu(ColorMenuWidth, ColorMenuHeight, "LyricsColorMenu");
            _sizeOptionImages.Clear();
            _colorOptionImages.Clear();
            DestroyChildrenExceptPanel(_tertiaryMenuRect);

            const int columns = 4;
            const float cellWidth = 50f;
            const float cellHeight = 39f;
            for (int i = TimelineColorPalette.MinAuthorColorId;
                 i <= TimelineColorPalette.MaxAuthorColorId;
                 i++)
            {
                int captured = i;
                int row = i / columns;
                int column = i % columns;
                GameObject item = CreateUiObject(
                    "Color" + captured,
                    _tertiaryMenuRect,
                    typeof(Image),
                    typeof(EventTrigger));
                RectTransform rect = item.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(42f, 31f);
                rect.anchoredPosition = new Vector2(
                    8f + column * cellWidth,
                    -8f - row * cellHeight);

                Image image = item.GetComponent<Image>();
                image.color = ResolveColor(captured);
                image.raycastTarget = true;
                _colorOptionImages.Add(image);

                TextMeshProUGUI label = CreateLabel(
                    item.transform,
                    captured.ToString(),
                    15f,
                    TextAlignmentOptions.Center,
                    false);
                label.color = GetReadableLabelColor(image.color);

                AddTrigger(item, EventTriggerType.PointerEnter, _ => TouchInteraction());
                AddTrigger(item, EventTriggerType.PointerClick, data =>
                {
                    PointerEventData pointer = data as PointerEventData;
                    if (pointer == null || pointer.button != PointerEventData.InputButton.Left)
                    {
                        return;
                    }

                    SetRuntimeColor(captured);
                });
            }

            RefreshMenuSelectionVisuals();
            PositionAndShowTertiaryMenu();
        }

        private void EnsureTertiaryMenu(float width, float height, string name)
        {
            if (_tertiaryMenuObject == null)
            {
                _tertiaryMenuObject = CreateUiObject(
                    name,
                    _root.transform,
                    typeof(Image),
                    typeof(EventTrigger));
                _tertiaryMenuRect = _tertiaryMenuObject.GetComponent<RectTransform>();
                _tertiaryMenuRect.anchorMin = new Vector2(0.5f, 1f);
                _tertiaryMenuRect.anchorMax = new Vector2(0.5f, 1f);
                _tertiaryMenuRect.pivot = new Vector2(0f, 1f);

                Image panel = _tertiaryMenuObject.GetComponent<Image>();
                panel.color = MenuPanelColor;
                panel.raycastTarget = true;
                AddTrigger(_tertiaryMenuObject, EventTriggerType.PointerEnter, _ => TouchInteraction());
            }
            else
            {
                _tertiaryMenuObject.name = name;
            }

            _tertiaryMenuRect.sizeDelta = new Vector2(width, height);
        }

        private void PositionAndShowTertiaryMenu()
        {
            if (_tertiaryMenuObject == null || _tertiaryMenuRect == null)
            {
                return;
            }

            float x = SecondaryMenuWidth * 0.5f + 8f;
            _tertiaryMenuRect.anchoredPosition = new Vector2(x, -ToolbarHeight);
            _tertiaryMenuObject.SetActive(true);
            TouchInteraction();
            ClampMenuToParent(_tertiaryMenuRect);
        }

        private void DestroyChildrenExceptPanel(RectTransform parent)
        {
            if (parent == null)
            {
                return;
            }

            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                child.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        private void SetRuntimeSize(int sizeMode)
        {
            if (_runtimeState == null)
            {
                return;
            }

            TouchInteraction();
            _runtimeState.SetRuntimeSize(sizeMode);
            ApplyStyle(_runtimeState.EffectiveSizeMode);
            RefreshCurrentLayoutAndClamp();
            RefreshMenuSelectionVisuals();
        }

        private void SetRuntimeColor(int colorMode)
        {
            if (_runtimeState == null)
            {
                return;
            }

            TouchInteraction();
            _runtimeState.SetRuntimeColor(colorMode);
            ApplyCurrentLineColor();
            RefreshMenuSelectionVisuals();
        }

        private void ResetRuntimeStyle()
        {
            if (_runtimeState == null)
            {
                return;
            }

            ShowSelectionFrame();
            TouchInteraction();
            _runtimeState.ResetRuntimeStyle();
            ApplyStyle(_runtimeState.EffectiveSizeMode);
            ApplyCurrentLineColor();
            RefreshCurrentLayoutAndClamp();
            RefreshMenuSelectionVisuals();
        }

        private void ApplyStyle(int sizeMode)
        {
            float scale;
            switch (sizeMode)
            {
                case 0:
                    scale = 0.8f;
                    break;
                case 2:
                    scale = 1.2f;
                    break;
                case 3:
                    scale = 1.5f;
                    break;
                case 4:
                    scale = 1.8f;
                    break;
                default:
                    scale = 1f;
                    break;
            }

            _fontScale = scale;
            _fontSize = BaseFontSize * scale;
            int initialColorMode = _runtimeState != null
                ? _runtimeState.InitialColorMode
                : 0;
            _baseColor = ResolveColor(initialColorMode);
            _baseColor.a = 0.98f;

            ApplyTextMetrics(_prefixText);
            ApplyTextMetrics(_lyricText);
            ApplyOutlineAndShadow();
        }

        private void ApplyCurrentLineColor()
        {
            Color color;
            if (_runtimeState != null && _runtimeState.HasColorOverride)
            {
                color = ResolveColor(_runtimeState.RuntimeColorMode);
            }
            else if (_currentLineColorMode >= 0)
            {
                color = ResolveColor(_currentLineColorMode);
            }
            else
            {
                color = _baseColor;
            }

            color.a = 0.98f;
            ApplyTextColor(_prefixText, color);
            ApplyTextColor(_lyricText, color);
            ApplyOutlineAndShadow();
        }

        private void RefreshCurrentLayoutAndClamp()
        {
            if (!string.IsNullOrEmpty(_currentKey))
            {
                RefreshLayout(
                    _currentPrefix,
                    _currentLyricBlock,
                    _currentHasSecondLine);
            }

            bool persist = _runtimeState != null && _runtimeState.HasCustomPosition;
            ClampFrameToParent(persist);
        }

        private void ApplyOutlineAndShadow()
        {
            Color outlineColor = _usingDarkOutline
                ? DarkOutlineColor
                : LightOutlineColor;

            ApplyEffects(_prefixText, outlineColor);
            ApplyEffects(_lyricText, outlineColor);
        }

        private void ApplyEffects(TextMeshProUGUI text, Color outlineColor)
        {
            if (text == null)
            {
                return;
            }

            // 细描边 + 轻微软阴影，避免旧版粗硬边缘，更接近桌面歌词常见效果。
            text.outlineColor = outlineColor;
            text.outlineWidth = 0.10f;

            Shadow shadow = text.GetComponent<Shadow>();
            if (shadow != null)
            {
                shadow.effectColor = new Color(0f, 0f, 0f, 0.44f);
                shadow.effectDistance = new Vector2(
                    1.7f * _fontScale,
                    -1.7f * _fontScale);
                shadow.useGraphicAlpha = true;
            }
        }

        private TextMeshProUGUI CreateText(
            string objectName,
            Transform parent,
            out RectTransform rect)
        {
            GameObject textObject = CreateUiObject(
                objectName,
                parent,
                typeof(TextMeshProUGUI),
                typeof(Shadow));

            rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0f, 1f);

            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            if (_ownerFont != null)
            {
                text.font = _ownerFont;
            }

            text.raycastTarget = false;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.fontStyle = FontStyles.Normal;
            text.outlineColor = DarkOutlineColor;
            text.outlineWidth = 0.10f;
            text.text = string.Empty;

            Shadow shadow = textObject.GetComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.44f);
            shadow.effectDistance = new Vector2(1.7f, -1.7f);
            shadow.useGraphicAlpha = true;
            return text;
        }

        private void ApplyTextMetrics(TextMeshProUGUI text)
        {
            if (text == null)
            {
                return;
            }

            text.fontSize = _fontSize;
        }

        private static void ApplyTextColor(TextMeshProUGUI text, Color color)
        {
            if (text != null)
            {
                text.color = color;
            }
        }

        private void RefreshLayout(
            string prefix,
            string lyricBlock,
            bool hasSecondLine)
        {
            if (_prefixText == null || _lyricText == null ||
                _prefixRect == null || _lyricRect == null || _rect == null)
            {
                return;
            }

            float parentWidth = _parentRect != null && _parentRect.rect.width > 1f
                ? _parentRect.rect.width
                : 1920f;
            float availableFrameWidth = Mathf.Max(
                160f,
                parentWidth - EdgePadding * 2f);
            float maximumFrameWidth = Mathf.Min(
                MaximumContentWidth + HorizontalPadding * 2f,
                availableFrameWidth);
            float minimumFrameWidth = Mathf.Min(
                MinimumFrameWidth,
                maximumFrameWidth);
            float maximumTextWidth = Mathf.Max(
                80f,
                maximumFrameWidth - HorizontalPadding * 2f);

            float textHeight = _fontSize * (hasSecondLine ? 2.55f : 1.45f);
            float prefixWidth = string.IsNullOrEmpty(prefix)
                ? 0f
                : Mathf.Min(
                    Mathf.Min(MaximumPrefixWidth, maximumTextWidth * 0.44f),
                    _prefixText.GetPreferredValues(prefix).x + 8f);
            float lyricWidth = Mathf.Min(
                maximumTextWidth - prefixWidth,
                _lyricText.GetPreferredValues(lyricBlock).x + 12f);
            lyricWidth = Mathf.Max(40f, lyricWidth);

            float totalTextWidth = Mathf.Min(
                maximumTextWidth,
                prefixWidth + lyricWidth);
            float frameWidth = Mathf.Clamp(
                totalTextWidth + HorizontalPadding * 2f,
                minimumFrameWidth,
                maximumFrameWidth);
            float frameHeight = ToolbarHeight + VerticalPadding * 2f + textHeight;
            _rect.sizeDelta = new Vector2(frameWidth, frameHeight);

            float startX = -totalTextWidth * 0.5f;
            float topY = -(ToolbarHeight + VerticalPadding);

            _prefixRect.sizeDelta = new Vector2(prefixWidth, textHeight);
            _prefixRect.anchoredPosition = new Vector2(startX, topY);
            _prefixText.gameObject.SetActive(prefixWidth > 0.01f);

            _lyricRect.sizeDelta = new Vector2(
                Mathf.Max(40f, totalTextWidth - prefixWidth),
                textHeight);
            _lyricRect.anchoredPosition = new Vector2(
                startX + prefixWidth,
                topY);
        }

        private void ShowSelectionFrame()
        {
            if (!IsAlive || string.IsNullOrEmpty(_currentKey))
            {
                return;
            }

            _frameVisible = true;
            _frameAutoHideAt = IsPositionLocked
                ? float.PositiveInfinity
                : Time.unscaledTime + FrameAutoHideSeconds;
            if (_frameBackground != null)
            {
                _frameBackground.color = VisibleFrameColor;
            }

            _toolbarRoot?.SetActive(true);
        }

        private void HideSelectionFrame(bool force = false)
        {
            if (!force && IsPositionLocked && !string.IsNullOrEmpty(_currentKey))
            {
                ShowSelectionFrame();
                HideMenus();
                return;
            }

            _frameVisible = false;
            _secondaryMenuVisible = false;
            if (_frameBackground != null)
            {
                _frameBackground.color = HiddenFrameColor;
            }

            _toolbarRoot?.SetActive(false);
            _secondaryMenuObject?.SetActive(false);
            _tertiaryMenuObject?.SetActive(false);
        }

        private void HideMenus()
        {
            _secondaryMenuVisible = false;
            _secondaryMenuObject?.SetActive(false);
            _tertiaryMenuObject?.SetActive(false);
        }

        private void TouchInteraction()
        {
            if (_frameVisible && !IsPositionLocked)
            {
                _frameAutoHideAt = Time.unscaledTime + FrameAutoHideSeconds;
            }
        }

        private void BeginDrag(BaseEventData data)
        {
            PointerEventData pointer = data as PointerEventData;
            if (pointer == null || pointer.button != PointerEventData.InputButton.Left ||
                _parentRect == null || _rect == null || IsPositionLocked)
            {
                return;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _parentRect,
                    pointer.position,
                    pointer.pressEventCamera,
                    out _dragStartPointerLocal))
            {
                return;
            }

            _dragStartAnchoredPosition = _rect.anchoredPosition;
            _isDragging = true;
            _dragMoved = false;
            ShowSelectionFrame();
            HideMenus();
            TouchInteraction();
        }

        private void Drag(BaseEventData data)
        {
            PointerEventData pointer = data as PointerEventData;
            if (!_isDragging || pointer == null || _parentRect == null || _rect == null)
            {
                return;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _parentRect,
                    pointer.position,
                    pointer.pressEventCamera,
                    out Vector2 currentPointerLocal))
            {
                return;
            }

            Vector2 delta = currentPointerLocal - _dragStartPointerLocal;
            _rect.anchoredPosition = _dragStartAnchoredPosition + delta;
            ClampFrameToParent(false);

            if (!_dragMoved && Vector2.Distance(_pointerDownScreenPosition, pointer.position) > DragThresholdPixels)
            {
                _dragMoved = true;
            }

            TouchInteraction();
        }

        private void EndDrag(BaseEventData data)
        {
            if (!_isDragging)
            {
                return;
            }

            _isDragging = false;
            ClampFrameToParent(false);
            _runtimeState?.SetPosition(_rect.anchoredPosition);
            TouchInteraction();
        }

        private void ClampFrameToParent(bool persistIfCustom)
        {
            if (_parentRect == null || _rect == null)
            {
                return;
            }

            Vector2 correction = CalculateRectCorrection(_rect, _parentRect, EdgePadding);
            if (correction.sqrMagnitude > 0.0001f)
            {
                _rect.anchoredPosition += correction;
            }

            if (persistIfCustom && _runtimeState != null &&
                (_runtimeState.HasCustomPosition || _runtimeState.IsPositionLocked))
            {
                _runtimeState.SetPosition(_rect.anchoredPosition);
            }
        }

        private void ClampMenuToParent(RectTransform menu)
        {
            if (menu == null || _parentRect == null)
            {
                return;
            }

            Vector2 correction = CalculateRectCorrection(menu, _parentRect, EdgePadding);
            if (correction.sqrMagnitude > 0.0001f)
            {
                menu.anchoredPosition += correction;
            }
        }

        private Vector2 CalculateRectCorrection(
            RectTransform target,
            RectTransform parent,
            float padding)
        {
            if (parent.rect.width <= 1f || parent.rect.height <= 1f)
            {
                return Vector2.zero;
            }

            target.GetWorldCorners(_worldCorners);
            float minX = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float minY = float.PositiveInfinity;
            float maxY = float.NegativeInfinity;

            for (int i = 0; i < _worldCorners.Length; i++)
            {
                Vector3 local = parent.InverseTransformPoint(_worldCorners[i]);
                minX = Mathf.Min(minX, local.x);
                maxX = Mathf.Max(maxX, local.x);
                minY = Mathf.Min(minY, local.y);
                maxY = Mathf.Max(maxY, local.y);
            }

            Rect safe = parent.rect;
            float safeMinX = safe.xMin + padding;
            float safeMaxX = safe.xMax - padding;
            float safeMinY = safe.yMin + padding;
            float safeMaxY = safe.yMax - padding;

            float width = maxX - minX;
            float height = maxY - minY;
            float safeWidth = safeMaxX - safeMinX;
            float safeHeight = safeMaxY - safeMinY;

            float correctionX;
            if (width >= safeWidth)
            {
                correctionX = (safeMinX + safeMaxX) * 0.5f - (minX + maxX) * 0.5f;
            }
            else if (minX < safeMinX)
            {
                correctionX = safeMinX - minX;
            }
            else if (maxX > safeMaxX)
            {
                correctionX = safeMaxX - maxX;
            }
            else
            {
                correctionX = 0f;
            }

            float correctionY;
            if (height >= safeHeight)
            {
                correctionY = (safeMinY + safeMaxY) * 0.5f - (minY + maxY) * 0.5f;
            }
            else if (minY < safeMinY)
            {
                correctionY = safeMinY - minY;
            }
            else if (maxY > safeMaxY)
            {
                correctionY = safeMaxY - maxY;
            }
            else
            {
                correctionY = 0f;
            }

            return new Vector2(correctionX, correctionY);
        }

        private void RefreshMenuSelectionVisuals()
        {
            if (_runtimeState == null)
            {
                return;
            }

            int effectiveSize = _runtimeState.EffectiveSizeMode;
            for (int i = 0; i < _sizeOptionImages.Count; i++)
            {
                Image image = _sizeOptionImages[i];
                if (image != null)
                {
                    image.color = i == effectiveSize
                        ? MenuItemSelectedColor
                        : MenuItemColor;
                }
            }

            int selectedColor = _runtimeState.HasColorOverride
                ? _runtimeState.RuntimeColorMode
                : (_runtimeState.UsesDynamicLineColors
                    ? -1
                    : _runtimeState.InitialColorMode);
            for (int i = 0; i < _colorOptionImages.Count; i++)
            {
                Image image = _colorOptionImages[i];
                if (image == null)
                {
                    continue;
                }

                Color baseColor = ResolveColor(i);
                float multiplier = i == selectedColor ? 1f : 0.76f;
                image.color = new Color(
                    baseColor.r * multiplier,
                    baseColor.g * multiplier,
                    baseColor.b * multiplier,
                    1f);
            }
        }

        private void AddInteractiveVisualTriggers(
            GameObject target,
            Image image,
            Color normal,
            Color hover)
        {
            AddTrigger(target, EventTriggerType.PointerEnter, _ =>
            {
                TouchInteraction();
                if (image != null)
                {
                    image.color = hover;
                }
            });
            AddTrigger(target, EventTriggerType.PointerExit, _ =>
            {
                if (image != null)
                {
                    image.color = normal;
                }
            });
        }

        private static void AddTrigger(
            GameObject target,
            EventTriggerType eventType,
            UnityAction<BaseEventData> callback)
        {
            EventTrigger trigger = target.GetComponent<EventTrigger>();
            if (trigger == null)
            {
                trigger = target.AddComponent<EventTrigger>();
            }

            if (trigger.triggers == null)
            {
                trigger.triggers = new List<EventTrigger.Entry>();
            }

            var entry = new EventTrigger.Entry
            {
                eventID = eventType,
                callback = new EventTrigger.TriggerEvent()
            };
            entry.callback.AddListener(callback);
            trigger.triggers.Add(entry);
        }

        private GameObject CreateUiObject(
            string name,
            Transform parent,
            params Type[] components)
        {
            var allComponents = new List<Type> { typeof(RectTransform) };
            if (components != null)
            {
                for (int i = 0; i < components.Length; i++)
                {
                    Type component = components[i];
                    if (component != null && component != typeof(RectTransform))
                    {
                        allComponents.Add(component);
                    }
                }
            }

            GameObject gameObject = new GameObject(name, allComponents.ToArray());
            gameObject.layer = _root != null ? _root.layer : 0;
            gameObject.transform.SetParent(parent, false);
            return gameObject;
        }


        private static void EnsureToolbarIconSprites()
        {
            if (_settingsIconSprite == null)
            {
                _settingsIconSprite = CreateProceduralIconSprite("BetterAudioGearIcon", DrawGearIcon);
            }

            if (_restoreIconSprite == null)
            {
                _restoreIconSprite = CreateProceduralIconSprite("BetterAudioRestoreIcon", DrawRestoreIcon);
            }

            if (_openLockIconSprite == null)
            {
                _openLockIconSprite = CreateProceduralIconSprite("BetterAudioOpenLockIcon", DrawOpenLockIcon);
            }

            if (_closedLockIconSprite == null)
            {
                _closedLockIconSprite = CreateProceduralIconSprite("BetterAudioClosedLockIcon", DrawClosedLockIcon);
            }
        }

        private Image CreateToolbarIcon(Transform parent, Sprite sprite)
        {
            GameObject iconObject = CreateUiObject(
                "Icon",
                parent,
                typeof(Image));
            RectTransform rect = iconObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(22f, 22f);
            rect.anchoredPosition = Vector2.zero;

            Image icon = iconObject.GetComponent<Image>();
            icon.sprite = sprite;
            icon.color = Color.white;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            return icon;
        }

        private static Sprite CreateProceduralIconSprite(
            string name,
            Action<Color32[], int> draw)
        {
            const int size = 64;
            var pixels = new Color32[size * size];
            draw?.Invoke(pixels, size);

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name + "Texture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                size);
            sprite.name = name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static void DrawGearIcon(Color32[] pixels, int size)
        {
            Color32 white = new Color32(255, 255, 255, 255);
            float center = (size - 1) * 0.5f;
            const float innerRadius = 11f;
            const float ringInner = 17f;
            const float ringOuter = 23f;
            const float toothInner = 22f;
            const float toothOuter = 29f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float radius = Mathf.Sqrt(dx * dx + dy * dy);
                    bool ring = radius >= ringInner && radius <= ringOuter;

                    float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                    if (angle < 0f)
                    {
                        angle += 360f;
                    }
                    float toothAngle = Mathf.Abs(Mathf.DeltaAngle(angle, Mathf.Round(angle / 45f) * 45f));
                    bool tooth = radius >= toothInner && radius <= toothOuter && toothAngle <= 8.5f;
                    bool hub = radius <= innerRadius;

                    // 中心保留一个透明孔，形成更接近网易云工具栏的线性齿轮效果。
                    if ((ring || tooth) && !hub)
                    {
                        pixels[y * size + x] = white;
                    }
                }
            }
        }

        private static void DrawRestoreIcon(Color32[] pixels, int size)
        {
            Color32 white = new Color32(255, 255, 255, 255);
            float center = (size - 1) * 0.5f;
            const float radius = 21f;
            const float thickness = 4.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                    if (angle < 0f)
                    {
                        angle += 360f;
                    }

                    // 逆时针复位：保留左上方箭头，圆环在箭头附近留出明确缺口。
                    bool arc = Mathf.Abs(r - radius) <= thickness &&
                               !(angle >= 128f && angle <= 188f);

                    // 箭头尖指向左侧，尾部接回圆环，避免小尺寸下看成普通圆圈。
                    bool arrowHead = dx >= -29f && dx <= -13f &&
                                     Mathf.Abs(dy - 9f) <= (dx + 29f) * 0.72f;
                    bool arrowTail = dx >= -17f && dx <= -8f &&
                                     dy >= 5f && dy <= 13f;

                    // 中心基准点表示“恢复到默认位置/默认状态”。
                    bool centerDot = dx * dx + dy * dy <= 5.5f * 5.5f;

                    if (arc || arrowHead || arrowTail || centerDot)
                    {
                        pixels[y * size + x] = white;
                    }
                }
            }
        }

        private static void DrawOpenLockIcon(Color32[] pixels, int size)
        {
            DrawLockIcon(pixels, size, false);
        }

        private static void DrawClosedLockIcon(Color32[] pixels, int size)
        {
            DrawLockIcon(pixels, size, true);
        }

        private static void DrawLockIcon(Color32[] pixels, int size, bool closed)
        {
            Color32 white = new Color32(255, 255, 255, 255);
            float center = (size - 1) * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;

                    // 锁体使用粗线框，和设置、复位图标保持统一的线性风格。
                    bool bodyOuter = Mathf.Abs(dx) <= 21f && dy >= -3f && dy <= 25f;
                    bool bodyInner = Mathf.Abs(dx) <= 15f && dy >= 3f && dy <= 19f;
                    bool bodyOutline = bodyOuter && !bodyInner;

                    // 锁孔：圆点加短柄，保证 22px 实际显示尺寸下仍然清晰。
                    bool keyCircle = dx * dx + (dy - 8f) * (dy - 8f) <= 4.8f * 4.8f;
                    bool keyStem = Mathf.Abs(dx) <= 2.2f && dy >= 8f && dy <= 16f;

                    bool shackle;
                    if (closed)
                    {
                        // 闭锁：锁梁居中并与锁体两侧完整连接。
                        float sx = dx;
                        float sy = dy + 3f;
                        float outer = sx * sx / (15f * 15f) + sy * sy / (18f * 18f);
                        float inner = sx * sx / (9f * 9f) + sy * sy / (12f * 12f);
                        shackle = outer <= 1.05f && inner >= 1f && dy <= 1f;
                    }
                    else
                    {
                        // 开锁：锁梁向左偏移，右侧明确断开并抬起，避免与闭锁状态混淆。
                        float sx = dx + 6f;
                        float sy = dy + 3f;
                        float outer = sx * sx / (15f * 15f) + sy * sy / (18f * 18f);
                        float inner = sx * sx / (9f * 9f) + sy * sy / (12f * 12f);
                        shackle = outer <= 1.05f && inner >= 1f && dy <= 1f;

                        // 移除右下连接段，形成真正的开口。
                        if (dx > 4f && dy > -13f)
                        {
                            shackle = false;
                        }

                        // 左侧锁梁仍与锁体连接。
                        bool leftConnector = dx >= -20f && dx <= -13f &&
                                             dy >= -4f && dy <= 3f;
                        shackle |= leftConnector;
                    }

                    if (bodyOutline || keyCircle || keyStem || shackle)
                    {
                        pixels[y * size + x] = white;
                    }
                }
            }
        }

        private TextMeshProUGUI CreateLabel(
            Transform parent,
            string text,
            float fontSize,
            TextAlignmentOptions alignment,
            bool useOwnerFont)
        {
            GameObject labelObject = CreateUiObject(
                "Label",
                parent,
                typeof(TextMeshProUGUI));
            RectTransform rect = labelObject.GetComponent<RectTransform>();
            StretchToParent(rect);

            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            if (useOwnerFont && _ownerFont != null)
            {
                label.font = _ownerFont;
            }

            label.text = text;
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = Color.white;
            label.fontStyle = FontStyles.Normal;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            return label;
        }

        private static void StretchToParent(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Color GetReadableLabelColor(Color background)
        {
            float luminance =
                background.r * 0.2126f +
                background.g * 0.7152f +
                background.b * 0.0722f;
            return luminance > 0.62f
                ? new Color(0.08f, 0.08f, 0.08f, 1f)
                : Color.white;
        }

        internal static Color ResolveColor(int colorMode)
        {
            return TimelineColorPalette.Resolve(colorMode);
        }
    }
}
