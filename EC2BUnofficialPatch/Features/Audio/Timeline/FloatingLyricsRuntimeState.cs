using UnityEngine;

namespace LFBetterAudio.Timeline
{
    /// <summary>
    /// 控制器生命周期内共享的位置锁状态。锁定后，新播放型 1163 也会沿用该位置；
    /// 字号、颜色仍由每个新会话自己的 EFFECT 初始参数决定。
    /// </summary>
    public sealed class FloatingLyricsPositionLockState
    {
        public bool IsLocked { get; private set; }
        public bool HasPosition { get; private set; }
        public Vector2 AnchoredPosition { get; private set; }

        public void Lock(Vector2 anchoredPosition)
        {
            AnchoredPosition = anchoredPosition;
            HasPosition = true;
            IsLocked = true;
        }

        public void Unlock()
        {
            IsLocked = false;
        }

        public void UpdateLockedPosition(Vector2 anchoredPosition)
        {
            if (!IsLocked)
            {
                return;
            }

            AnchoredPosition = anchoredPosition;
            HasPosition = true;
        }
    }

    /// <summary>
    /// 当前 1163 播放会话内的浮动歌词交互状态。
    /// 背景音乐跨 Talk 时复用；新的播放型 1163 会创建全新字号/颜色状态，
    /// 但会读取控制器共享的位置锁状态。
    /// </summary>
    public sealed class FloatingLyricsRuntimeState
    {
        public FloatingLyricsRuntimeState(
            int initialSizeMode,
            int initialColorMode,
            bool usesDynamicLineColors,
            FloatingLyricsPositionLockState positionLockState = null)
        {
            InitialSizeMode = NormalizeVisibleSizeMode(initialSizeMode);
            InitialColorMode = TimelineColorPalette.NormalizeAuthorColorId(initialColorMode);
            RuntimeSizeMode = InitialSizeMode;
            RuntimeColorMode = InitialColorMode;
            UsesDynamicLineColors = usesDynamicLineColors;
            PositionLockState = positionLockState;
        }

        public int InitialSizeMode { get; }
        public int InitialColorMode { get; }
        public bool UsesDynamicLineColors { get; }
        public FloatingLyricsPositionLockState PositionLockState { get; }

        public int RuntimeSizeMode { get; private set; }
        public int RuntimeColorMode { get; private set; }
        public bool HasSizeOverride { get; private set; }
        public bool HasColorOverride { get; private set; }

        public Vector2 AnchoredPosition { get; private set; }
        public bool HasCustomPosition { get; private set; }
        public bool IsPositionLocked => PositionLockState != null && PositionLockState.IsLocked;

        public int EffectiveSizeMode => HasSizeOverride
            ? RuntimeSizeMode
            : InitialSizeMode;

        public void SetRuntimeSize(int sizeMode)
        {
            RuntimeSizeMode = NormalizeVisibleSizeMode(sizeMode);
            HasSizeOverride = true;
        }

        public void SetRuntimeColor(int colorMode)
        {
            RuntimeColorMode = TimelineColorPalette.NormalizeAuthorColorId(colorMode);
            HasColorOverride = true;
        }

        public void ResetRuntimeStyle()
        {
            RuntimeSizeMode = InitialSizeMode;
            RuntimeColorMode = InitialColorMode;
            HasSizeOverride = false;
            HasColorOverride = false;
        }

        public void SetPosition(Vector2 anchoredPosition)
        {
            AnchoredPosition = anchoredPosition;
            HasCustomPosition = true;
            PositionLockState?.UpdateLockedPosition(anchoredPosition);
        }

        public void LockPosition(Vector2 anchoredPosition)
        {
            SetPosition(anchoredPosition);
            PositionLockState?.Lock(anchoredPosition);
        }

        public void UnlockPosition()
        {
            PositionLockState?.Unlock();
        }

        public bool TryGetPreferredPosition(out Vector2 anchoredPosition)
        {
            if (PositionLockState != null &&
                PositionLockState.IsLocked &&
                PositionLockState.HasPosition)
            {
                anchoredPosition = PositionLockState.AnchoredPosition;
                return true;
            }

            if (HasCustomPosition)
            {
                anchoredPosition = AnchoredPosition;
                return true;
            }

            anchoredPosition = default(Vector2);
            return false;
        }

        private static int NormalizeVisibleSizeMode(int sizeMode)
        {
            // 0 是运行时菜单的小字号（0.8 倍）；1~4 保持 1163 的可见字号语义。
            return Mathf.Clamp(sizeMode, 0, 4);
        }
    }
}
