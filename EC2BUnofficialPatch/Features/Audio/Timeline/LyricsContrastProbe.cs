using System.Collections;
using UnityEngine;

namespace LFBetterAudio.Timeline
{
    /// <summary>
    /// 轻量读取歌词区域下方的一小条屏幕像素，用于估算局部背景亮度。
    /// 只在歌词可见时、每隔固定时间采样一次，不分析整张画面。
    /// </summary>
    public sealed class LyricsContrastProbe : MonoBehaviour
    {
        private const float SampleIntervalSeconds = 0.35f;
        private const int SampleWidth = 64;
        private const int SampleHeight = 4;
        private const float SampleBottomPadding = 18f;

        private readonly Vector3[] _worldCorners = new Vector3[4];
        private readonly WaitForEndOfFrame _waitForEndOfFrame = new WaitForEndOfFrame();

        private FloatingLyricsOverlay _owner;
        private RectTransform _target;
        private Canvas _canvas;
        private Texture2D _sampleTexture;
        private Coroutine _samplingCoroutine;

        internal void Configure(FloatingLyricsOverlay owner, RectTransform target)
        {
            _owner = owner;
            _target = target;
            _canvas = target != null ? target.GetComponentInParent<Canvas>() : null;

            if (_samplingCoroutine == null)
            {
                _samplingCoroutine = StartCoroutine(SampleLoop());
            }
        }

        private IEnumerator SampleLoop()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(SampleIntervalSeconds);

                if (_owner == null || _target == null || !_owner.CanSampleContrast)
                {
                    continue;
                }

                yield return _waitForEndOfFrame;
                TrySampleBackground();
            }
        }

        private void TrySampleBackground()
        {
            try
            {
                if (Screen.width < SampleWidth || Screen.height < SampleHeight)
                {
                    return;
                }

                _target.GetWorldCorners(_worldCorners);
                Camera camera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? _canvas.worldCamera
                    : null;

                Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(camera, _worldCorners[0]);
                Vector2 topRight = RectTransformUtility.WorldToScreenPoint(camera, _worldCorners[2]);
                float centerX = (bottomLeft.x + topRight.x) * 0.5f;

                int x = Mathf.Clamp(
                    Mathf.RoundToInt(centerX - SampleWidth * 0.5f),
                    0,
                    Screen.width - SampleWidth);
                int y = Mathf.Clamp(
                    Mathf.RoundToInt(bottomLeft.y + SampleBottomPadding),
                    0,
                    Screen.height - SampleHeight);

                if (_sampleTexture == null)
                {
                    _sampleTexture = new Texture2D(
                        SampleWidth,
                        SampleHeight,
                        TextureFormat.RGB24,
                        false);
                }

                _sampleTexture.ReadPixels(
                    new Rect(x, y, SampleWidth, SampleHeight),
                    0,
                    0,
                    false);
                _sampleTexture.Apply(false, false);

                Color32[] pixels = _sampleTexture.GetPixels32();
                if (pixels == null || pixels.Length == 0)
                {
                    return;
                }

                double luminanceSum = 0d;
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color32 pixel = pixels[i];
                    luminanceSum +=
                        (pixel.r / 255d) * 0.2126d +
                        (pixel.g / 255d) * 0.7152d +
                        (pixel.b / 255d) * 0.0722d;
                }

                _owner.ApplyBackgroundLuminance(
                    (float)(luminanceSum / pixels.Length));
            }
            catch
            {
                // 某些图形后端不允许读取屏幕像素时，保留默认黑色描边。
            }
        }

        private void OnDestroy()
        {
            if (_sampleTexture != null)
            {
                Destroy(_sampleTexture);
                _sampleTexture = null;
            }

            _owner = null;
            _target = null;
            _canvas = null;
            _samplingCoroutine = null;
        }
    }
}
