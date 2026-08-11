using DG.Tweening;
using EC2BUnofficialPatch.Core;
using UnityEngine;
using UnityEngine.UI;

namespace EC2BUnofficialPatch.Features.Optimization.CGOptimization
{
    internal sealed class CGHoldLayer : MonoBehaviour
    {
        private const float UnexpectedDisableGraceSeconds = 1f;

        private int _ownerId;
        private RectTransform _rootCg;
        private RectTransform _sourceRect;
        private Image _sourceImage;
        private RectTransform _rect;
        private Image _image;
        private CanvasGroup _group;
        private float _lastHeartbeatRealtime;
        private float _sourceDisabledRealtime = -1f;
        private bool _explicitExit;

        internal Sprite Sprite => _image != null ? _image.sprite : null;
        internal bool HasSprite => Sprite != null && gameObject.activeSelf;

        internal void Initialize(
            int ownerId,
            RectTransform rootCg,
            RectTransform sourceRect,
            Image sourceImage,
            RectTransform rect,
            Image image,
            CanvasGroup group)
        {
            _ownerId = ownerId;
            _rootCg = rootCg;
            _sourceRect = sourceRect;
            _sourceImage = sourceImage;
            _rect = rect;
            _image = image;
            _group = group;
            _lastHeartbeatRealtime = Time.realtimeSinceStartup;

            gameObject.SetActive(false);
            SyncGeometry();
            MirrorFrom(sourceImage);
        }

        internal void Heartbeat()
        {
            _lastHeartbeatRealtime = Time.realtimeSinceStartup;
            _sourceDisabledRealtime = -1f;
        }

        internal void NotifySourceDisabled()
        {
            if (_sourceDisabledRealtime < 0f)
            {
                _sourceDisabledRealtime = Time.realtimeSinceStartup;
            }
        }

        internal void SetSprite(Sprite sprite, string reason)
        {
            if (sprite == null || _image == null || _group == null)
            {
                return;
            }

            _group.DOKill(false);
            _explicitExit = false;
            _image.sprite = sprite;
            MirrorFrom(_sourceImage);
            SyncGeometry();
            _group.alpha = 1f;
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }

            PatchLog.Debug(
                "优化模块-CG优化保底层更新：" +
                $"controller={_ownerId}, sprite={CGTransitionController.Describe(sprite)}, reason={reason}");
        }

        internal void CancelExit()
        {
            if (_group == null)
            {
                return;
            }

            _group.DOKill(false);
            _explicitExit = false;
            if (_image != null && _image.sprite != null)
            {
                _group.alpha = 1f;
                if (!gameObject.activeSelf)
                {
                    gameObject.SetActive(true);
                }
            }
        }

        internal void FadeOutAndClear(float duration, string reason)
        {
            if (_group == null || _image == null || _image.sprite == null)
            {
                ClearImmediate(reason);
                return;
            }

            _group.DOKill(false);
            _explicitExit = true;
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }

            if (duration <= 0f)
            {
                ClearImmediate(reason);
                return;
            }

            _group
                .DOFade(0f, duration)
                .SetUpdate(true)
                .OnComplete(() => ClearImmediate(reason));
        }

        internal void ClearImmediate(string reason)
        {
            if (_group != null)
            {
                _group.DOKill(false);
                _group.alpha = 0f;
            }

            if (_image != null)
            {
                _image.sprite = null;
            }

            _explicitExit = false;
            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }

            PatchLog.Debug(
                $"优化模块-CG优化保底层已清理：controller={_ownerId}, reason={reason}");
        }

        internal void MirrorFrom(Image source)
        {
            CGTransitionController.MirrorImageState(_image, source);
        }

        private void LateUpdate()
        {
            if (_rect == null || _rootCg == null)
            {
                return;
            }

            // 显式退出淡出时冻结最后一帧的几何和颜色，避免 sourceRect 复位到 1.0 后保底图突然缩小。
            if (_explicitExit)
            {
                return;
            }

            SyncGeometry();
            MirrorFrom(_sourceImage);

            if (!HasSprite || _sourceRect == null ||
                _sourceRect.gameObject.activeInHierarchy)
            {
                return;
            }

            float disabledSince = _sourceDisabledRealtime >= 0f
                ? _sourceDisabledRealtime
                : _lastHeartbeatRealtime;

            if (Time.realtimeSinceStartup - disabledSince > UnexpectedDisableGraceSeconds)
            {
                ClearImmediate("CG对象长期失活，防止保底层残留");
            }
        }

        private void SyncGeometry()
        {
            if (_rect == null || _sourceRect == null || _rootCg == null || _rootCg.parent == null)
            {
                return;
            }

            if (_rect.parent != _rootCg.parent)
            {
                _rect.SetParent(_rootCg.parent, false);
            }

            int targetSibling = Mathf.Max(0, _rootCg.GetSiblingIndex() - 1);
            if (_rect.GetSiblingIndex() != targetSibling)
            {
                _rect.SetSiblingIndex(targetSibling);
            }

            _rect.anchorMin = new Vector2(0.5f, 0.5f);
            _rect.anchorMax = new Vector2(0.5f, 0.5f);
            _rect.pivot = _sourceRect.pivot;
            _rect.sizeDelta = _sourceRect.rect.size;
            _rect.position = _sourceRect.position;
            _rect.rotation = _sourceRect.rotation;

            Vector3 parentScale = _rect.parent.lossyScale;
            Vector3 sourceScale = _sourceRect.lossyScale;
            _rect.localScale = new Vector3(
                SafeDivide(sourceScale.x, parentScale.x),
                SafeDivide(sourceScale.y, parentScale.y),
                SafeDivide(sourceScale.z, parentScale.z));
        }

        private static float SafeDivide(float value, float divisor)
        {
            return Mathf.Abs(divisor) < 0.0001f ? value : value / divisor;
        }
    }

}
