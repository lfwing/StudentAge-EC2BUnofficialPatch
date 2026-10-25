using System;
using System.Linq;
using DG.Tweening;
using EC2BUnofficialPatch.Core;
using Sdk;
using UnityEngine;
using UnityEngine.UI;
using View.Main;
using Object = UnityEngine.Object;

namespace EC2BUnofficialPatch.Features.Mechanics.MapExtend
{
    // Hold the outgoing backdrop until the incoming map is ready. Native scene
    // controls continue their normal fade; the detached copy owns its texture.
    internal static class WorldMapTransitions
    {
        private static GameObject _pending;
        private static GameObject _shown;
        private static BaseView _owner;
        private static Vector3[] _corners;

        internal static void Capture(BaseView view)
        {
            Clear();
            try
            {
                Graphic source = view is MapSceneView scene ? scene.icon_bg?.image
                    : view.gameObject.GetComponent<WorldMapSession>()?.BackgroundImage;
                if (source == null) source = view.gameObject.GetComponentsInChildren<Graphic>(false)
                    .Where(graphic => graphic.enabled && ((graphic is Image img && img.sprite != null && img.sprite.rect.width >= 512 && img.sprite.rect.height >= 256) ||
                        (graphic is RawImage raw && raw.texture != null && raw.texture.width >= 512 && raw.texture.height >= 256)))
                    .Where(graphic => graphic.rectTransform.rect.width >= 512 && graphic.rectTransform.rect.height >= 256)
                    .OrderByDescending(graphic => graphic.name.IndexOf("bg", StringComparison.OrdinalIgnoreCase) >= 0 || graphic.name.IndexOf("background", StringComparison.OrdinalIgnoreCase) >= 0)
                    .ThenByDescending(graphic => graphic.rectTransform.rect.width * graphic.rectTransform.rect.height).FirstOrDefault();
                if (source == null) return;
                _corners = new Vector3[4]; source.rectTransform.GetWorldCorners(_corners);
                _pending = new GameObject("EC2B.WorldMapDissolve", typeof(RectTransform), typeof(CanvasGroup));
                _pending.SetActive(false);
                var lifetime = _pending.AddComponent<WorldMapSnapshotLifetime>();
                if (source is Image image)
                {
                    lifetime.Texture = CopyTexture(image.sprite.texture);
                    lifetime.Sprite = Sprite.Create((Texture2D)lifetime.Texture, image.sprite.rect, new Vector2(image.sprite.pivot.x / image.sprite.rect.width, image.sprite.pivot.y / image.sprite.rect.height),
                        image.sprite.pixelsPerUnit, 0, SpriteMeshType.FullRect, image.sprite.border);
                    Image copy = _pending.AddComponent<Image>(); copy.sprite = lifetime.Sprite; copy.color = image.color;
                    copy.type = image.type; copy.preserveAspect = image.preserveAspect; copy.raycastTarget = false;
                }
                else if (source is RawImage raw)
                {
                    lifetime.Texture = CopyTexture(raw.texture);
                    RawImage copy = _pending.AddComponent<RawImage>(); copy.texture = lifetime.Texture;
                    copy.uvRect = raw.uvRect; copy.color = raw.color; copy.raycastTarget = false;
                }
            }
            catch (Exception exception)
            {
                Clear();
                // A cosmetic transition must not interrupt native scene closing.
                PatchLog.Warning("机制模块-大地图过渡背景复制失败，继续正常切换地图：" + ModuleHost.GetReason(exception));
            }
        }

        private static Texture2D CopyTexture(Texture source)
        {
            if (source == null || source.width <= 0 || source.height <= 0)
                throw new InvalidOperationException("过渡背景纹理无效。");

            // Native Addressables textures may have no CPU data. Blit their GPU
            // content to a temporary target instead of instantiating the texture.
            RenderTexture previous = RenderTexture.active;
            RenderTexture target = null;
            Texture2D copy = null;
            try
            {
                target = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
                copy.Apply(false, false);
                copy.filterMode = source.filterMode;
                copy.wrapMode = source.wrapMode;
                return copy;
            }
            catch
            {
                if (copy != null) Object.Destroy(copy);
                throw;
            }
            finally
            {
                RenderTexture.active = previous;
                if (target != null) RenderTexture.ReleaseTemporary(target);
            }
        }
        internal static void Attach(BaseView view)
        {
            if (_pending == null) return;
            ClearShown();
            GameObject node = _pending;
            _pending = null; _shown = node; _owner = view;
            // The copy must outlive the closing scene and remain outside the
            // incoming view's CanvasGroup while its native fade runs.
            Transform layer = UIMgr.GetLayerXform(view.layerType);
            var rect = (RectTransform)node.transform;
            rect.SetParent(layer, false); rect.SetAsLastSibling();
            Vector3 a = layer.InverseTransformPoint(_corners[0]), b = layer.InverseTransformPoint(_corners[2]);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f); rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y)); rect.localPosition = (a + b) * 0.5f;
            var group = node.GetComponent<CanvasGroup>(); group.ignoreParentGroups = true;
            group.blocksRaycasts = false; group.interactable = false; group.alpha = 1;
            Canvas layerCanvas = layer.GetComponent<Canvas>();
            Canvas canvas = node.AddComponent<Canvas>();
            canvas.overrideSorting = true; canvas.sortingLayerID = layerCanvas.sortingLayerID;
            canvas.sortingOrder = layerCanvas.sortingOrder + 1;
            node.SetActive(true);
        }
        internal static void Fade(BaseView view)
        {
            if (_shown == null || _owner != view) return;
            GameObject node = _shown;
            var group = node.GetComponent<CanvasGroup>();
            group.DOFade(0, 0.2f).SetEase(Ease.Linear).OnComplete(() =>
            {
                if (_shown == node) { _shown = null; _owner = null; }
                Object.Destroy(node);
            });
        }
        internal static void Release(BaseView view)
        {
            if (_owner == view) ClearShown();
        }
        private static void ClearShown()
        {
            Dispose(ref _shown);
            _owner = null;
        }
        internal static void Clear()
        {
            Dispose(ref _pending);
            ClearShown();
            _corners = null;
        }
        private static void Dispose(ref GameObject node)
        {
            if (node != null)
            {
                node.GetComponent<WorldMapSnapshotLifetime>().ReleaseAssets();
                node.SetActive(false);
                Object.Destroy(node);
            }
            node = null;
        }
    }
    internal sealed class WorldMapSnapshotLifetime : MonoBehaviour
    {
        internal Texture Texture;
        internal Sprite Sprite;
        private void OnDestroy() => ReleaseAssets();
        internal void ReleaseAssets()
        {
            GetComponent<CanvasGroup>().DOKill(false);
            if (Sprite != null) Object.Destroy(Sprite);
            if (Texture != null) Object.Destroy(Texture);
            Sprite = null; Texture = null;
        }
    }
    internal sealed class WorldMapReturnDriver : MonoBehaviour
    {
        private void LateUpdate() => WorldMapsRuntime.ReturnIfRemoved();
    }
}
