using System;
using System.Collections.Generic;
using System.Linq;
using EC2BUnofficialPatch.Resources;
using UnityEngine;
using UnityEngine.UI;
using View.Main;
using Object = UnityEngine.Object;

namespace EC2BUnofficialPatch.Features.Mechanics.MapExtend
{
    internal sealed class WorldMapSession : MonoBehaviour
    {
        private readonly List<Transform> _natives = new List<Transform>();
        private readonly Dictionary<Graphic, bool> _nativeBackgrounds = new Dictionary<Graphic, bool>();
        private RectTransform _group;
        private GameObject _stash, _background;
        private TextureCache _textures;
        internal RectTransform LabelTemplate { get; private set; }
        internal Image BackgroundImage { get; private set; }

        internal void Prepare(MapView view)
        {
            Release();
            _group = view.group_item;
            Transform[] natives = Enumerable.Range(0, _group.childCount).Select(_group.GetChild).ToArray();
            LabelTemplate = natives.Select(node => node.Find("btn_name") as RectTransform)
                .Where(node => node != null && node.rect.width > 0 && node.rect.height > 0 && node.Find("img_name/txt_name") != null)
                .OrderByDescending(node => node.rect.width / node.rect.height).FirstOrDefault();
            // Overview art can live on group_item or a sibling. Never disable building/label art.
            Graphic[] backgrounds = view.gameObject.GetComponentsInChildren<Graphic>(true)
                .Where(graphic => !natives.Any(node => graphic.transform.IsChildOf(node)) && IsOverview(graphic))
                .ToArray();
            Image nativeImage = backgrounds.OfType<Image>().Where(image => image.enabled && image.gameObject.activeInHierarchy)
                .OrderByDescending(image => image.rectTransform.rect.width * image.rectTransform.rect.height).FirstOrDefault();
            if (nativeImage != null) WorldMapsRuntime.NativeOverviewSprite = nativeImage.sprite;
            WorldMapDefinition world = WorldMapsRuntime.Worlds.FirstOrDefault(item => item.Id == WorldMapsRuntime.ActiveWorldId);
            if (world == null || world.Id == 0) return;
            if (LabelTemplate == null) throw new InvalidOperationException("原版地图标签模板未加载。");
            _textures = new TextureCache();
            if (!_textures.TryGetSprite(world.ImagePath, out Sprite sprite))
                throw new InvalidOperationException("大地图底图读取失败：" + world.ImagePath);
            foreach (Graphic graphic in backgrounds)
            {
                _nativeBackgrounds.Add(graphic, graphic.enabled);
                graphic.enabled = false;
            }
            // MapView parses every direct child even when inactive; move other-world nodes outside it.
            _stash = new GameObject("EC2B.NativeMapNodes", typeof(RectTransform));
            _stash.transform.SetParent(view.transform, false);
            _stash.SetActive(false);
            foreach (Transform native in natives)
            {
                _natives.Add(native);
                native.SetParent(_stash.transform, false);
            }
            var background = new GameObject("EC2B.WorldBackground", typeof(RectTransform)).GetComponent<RectTransform>();
            _background = background.gameObject;
            background.SetParent(_group.parent, false);
            background.anchorMin = background.anchorMax = new Vector2(0.5f, 0.5f);
            background.pivot = new Vector2(0.5f, 0.5f);
            background.localRotation = _group.localRotation; background.localScale = _group.localScale;
            background.sizeDelta = world.Size; background.localPosition = _group.localPosition;
            // Render directly beneath this world's markers after hiding all native overview graphics.
            background.SetSiblingIndex(_group.GetSiblingIndex());
            BackgroundImage = _background.AddComponent<Image>();
            BackgroundImage.sprite = sprite; BackgroundImage.color = Color.white;
            BackgroundImage.type = Image.Type.Simple; BackgroundImage.preserveAspect = false;
            BackgroundImage.raycastTarget = false;
        }
        private static bool IsOverview(Graphic graphic)
        {
            if (graphic is Image image)
                return image.sprite != null && image.sprite.rect.width >= 512 && image.sprite.rect.height >= 256 &&
                    image.rectTransform.rect.width >= 512 && image.rectTransform.rect.height >= 256;
            if (graphic is RawImage raw)
                return raw.texture != null && raw.texture.width >= 512 && raw.texture.height >= 256 &&
                    raw.rectTransform.rect.width >= 512 && raw.rectTransform.rect.height >= 256;
            return false;
        }
        internal void Release()
        {
            foreach (Transform native in _natives) if (native != null && _group != null) native.SetParent(_group, false);
            _natives.Clear();
            foreach (KeyValuePair<Graphic, bool> graphic in _nativeBackgrounds)
                if (graphic.Key != null) graphic.Key.enabled = graphic.Value;
            _nativeBackgrounds.Clear();
            DisposeNode(ref _stash); DisposeNode(ref _background);
            BackgroundImage = null;
            _textures?.Dispose(); _textures = null; LabelTemplate = null;
        }
        private static void DisposeNode(ref GameObject node)
        {
            if (node != null) { node.SetActive(false); node.transform.SetParent(null, false); Object.Destroy(node); }
            node = null;
        }
        private void OnDestroy() => Release();
    }
}
