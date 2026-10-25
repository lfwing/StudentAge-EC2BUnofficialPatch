using System;
using System.Collections.Generic;
using EC2BUnofficialPatch.Core;
using HarmonyLib;
using Sdk;
using UnityEngine;
using UnityEngine.UI;
using View.Event;
using View.Main;

namespace EC2BUnofficialPatch.Features.Mechanics.MapExtend
{
    // Only object introductions use this presentation. Native queued events and
    // unrelated CommonTalkView dialogs keep their original background handling.
    internal static class SceneObjectIntroduction
    {
        internal static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(CommonTalkView), "OnOpen", Type.EmptyTypes),
                postfix: new HarmonyMethod(typeof(SceneObjectIntroduction), nameof(Opened)));
            // OnClose is inherited; the callback must accept other NewTalkView instances too.
            harmony.Patch(AccessTools.Method(typeof(CommonTalkView), "OnClose", Type.EmptyTypes),
                prefix: new HarmonyMethod(typeof(SceneObjectIntroduction), nameof(Closed)));
        }

        internal static object[] Parameters(int role, string text, Action callback, int face, int emoji,
            SceneObjectSession source) => new object[] { role, text, callback, face, emoji, -1, source };

        public static void ShowTalk(int role, string text, Action callback, int face, int emoji, int bg, BaseView view)
        {
            SceneObjectSession source = view?.gameObject?.GetComponent<SceneObjectSession>();
            if (source == null) { HintHelper.ShowTalk(role, text, callback, face, emoji, bg); return; }
            source.Refresh();
            source.EndHover();
            // The source is carried with this particular opening, including asynchronous UI loading.
            UIMgr.OpenView<CommonTalkView>(UILayerType.None, null,
                Parameters(role, text, callback, face, emoji, source));
        }

        public static void Opened(CommonTalkView __instance)
        {
            SceneIntroductionVisual visual = __instance.gameObject.GetComponent<SceneIntroductionVisual>();
            visual?.Release();
            if (__instance.parms == null || __instance.parms.Length < 7 ||
                !(__instance.parms[6] is SceneObjectSession source) || source == null) return;
            if (visual == null) visual = __instance.gameObject.AddComponent<SceneIntroductionVisual>();
            visual.Bind(__instance, source);
        }
        public static void Closed(BaseView __instance) => __instance.gameObject?.GetComponent<SceneIntroductionVisual>()?.Release();
    }

    // Home keeps its live backdrop behind the native blur mask. Map scenes use
    // the already displayed sprite in the dialog's existing background, with
    // noninteractive ordinary images as children on that background's plane.
    internal sealed class SceneIntroductionVisual : MonoBehaviour
    {
        private SceneObjectSession _source;
        private CommonTalkView _talk;
        private RectTransform _root;
        private ImageState _savedImage;
        private bool _changedBackground;
        private RectState _savedRect;
        private readonly Dictionary<Image, Image> _images = new Dictionary<Image, Image>();
        private readonly List<Image> _wanted = new List<Image>(), _stale = new List<Image>();

        internal void Bind(CommonTalkView talk, SceneObjectSession source)
        {
            Release(); _source = source; _talk = talk; source.Introduction = this;
            if (source.View is MapSceneView)
            {
                Image image = talk.icon_bg.image;
                _savedImage = new ImageState(image);
                _savedRect = new RectState(image.rectTransform); _changedBackground = true;
                var go = new GameObject("UP.SceneIntroduction", typeof(RectTransform));
                go.layer = image.gameObject.layer;
                _root = (RectTransform)go.transform;
                _root.SetParent(image.transform, false);
                _root.anchorMin = Vector2.zero; _root.anchorMax = Vector2.one;
                _root.offsetMin = _root.offsetMax = Vector2.zero;
                _root.SetAsLastSibling();
            }
            Sync();
        }
        private void LateUpdate() { if (_source != null) Sync(); }
        private void Sync()
        {
            try
            {
                if (_source == null || _source.View?.gameObject == null || !_source.View.gameObject.activeInHierarchy)
                { Release(); return; }
                _source.EndHover();
                if (_source.View is MainView) return; // no foreground backdrop; preserve native home blur
                _wanted.Clear(); _stale.Clear();
                Image background = _source.BackgroundImage;
                if (background == null || background.sprite == null) return;
                CopyImage(background, _talk.icon_bg.image);
                _talk.icon_bg.gameObject.SetActive(true);
                _wanted.AddRange(_source.IntroductionImages());
                foreach (Image image in _images.Keys)
                    if (image == null || !_wanted.Contains(image)) _stale.Add(image);
                foreach (Image stale in _stale)
                {
                    _images[stale].gameObject.SetActive(false);
                    Destroy(_images[stale].gameObject); _images.Remove(stale);
                }
                foreach (Image image in _wanted)
                {
                    if (!_images.TryGetValue(image, out Image copy))
                    {
                        var go = new GameObject("visual", typeof(RectTransform), typeof(Image));
                        go.layer = _root.gameObject.layer;
                        go.transform.SetParent(_root, false); copy = go.GetComponent<Image>();
                        copy.raycastTarget = false; _images.Add(image, copy);
                    }
                    copy.transform.SetAsLastSibling();
                    CopyImage(image, copy);
                }
            }
            catch (Exception ex)
            {
                Release();
                PatchLog.Warning("场景介绍图片同步失败，保留原版介绍：" + ModuleHost.GetReason(ex));
            }
        }
        private static void CopyImage(Image source, Image target)
        {
            target.sprite = source.sprite; target.color = source.color;
            target.material = source.material; target.type = source.type;
            target.preserveAspect = source.preserveAspect; target.fillCenter = source.fillCenter;
            target.fillMethod = source.fillMethod; target.fillAmount = source.fillAmount;
            target.fillClockwise = source.fillClockwise; target.fillOrigin = source.fillOrigin;
            RectTransform from = source.rectTransform, to = target.rectTransform;
            to.anchorMin = to.anchorMax = new Vector2(.5f, .5f); to.pivot = from.pivot;
            to.sizeDelta = from.rect.size;
            // UIMgr places views/masks at different Z depths. Only copy the 2D
            // placement; world Z would put a copy back on the old scene plane.
            to.localPosition = Flatten(to.parent.InverseTransformPoint(from.position));
            to.rotation = from.rotation;
            Vector3 parent = to.parent.lossyScale, scale = from.lossyScale;
            if (Mathf.Abs(parent.x) > .00001f && Mathf.Abs(parent.y) > .00001f)
                to.localScale = new Vector3(scale.x / parent.x, scale.y / parent.y, 1f);
        }
        internal static Vector3 Flatten(Vector3 point) => new Vector3(point.x, point.y, 0f);
        internal void Release()
        {
            if (_source != null && _source.Introduction == this) _source.Introduction = null;
            _source = null;
            if (_root != null) { _root.gameObject.SetActive(false); Destroy(_root.gameObject); _root = null; }
            _images.Clear(); _wanted.Clear(); _stale.Clear();
            if (_changedBackground && _talk?.icon_bg?.image != null)
            {
                Image image = _talk.icon_bg.image;
                _savedImage.Restore(image);
                _savedRect.Restore(image.rectTransform);
                image.gameObject.SetActive(false);
            }
            _talk = null; _savedImage = default; _changedBackground = false;
        }
        private readonly struct ImageState
        {
            private readonly Sprite _sprite;
            private readonly Color _color;
            private readonly Material _material;
            private readonly Image.Type _type;
            private readonly bool _aspect, _center, _clockwise;
            private readonly Image.FillMethod _fillMethod;
            private readonly float _fillAmount;
            private readonly int _fillOrigin;
            internal ImageState(Image image)
            {
                _sprite = image.sprite; _color = image.color; _material = image.material; _type = image.type;
                _aspect = image.preserveAspect; _center = image.fillCenter; _clockwise = image.fillClockwise;
                _fillMethod = image.fillMethod; _fillAmount = image.fillAmount; _fillOrigin = image.fillOrigin;
            }
            internal void Restore(Image image)
            {
                image.sprite = _sprite; image.color = _color; image.material = _material; image.type = _type;
                image.preserveAspect = _aspect; image.fillCenter = _center; image.fillClockwise = _clockwise;
                image.fillMethod = _fillMethod; image.fillAmount = _fillAmount; image.fillOrigin = _fillOrigin;
            }
        }
        private readonly struct RectState
        {
            private readonly Vector2 _min, _max, _pivot, _size;
            private readonly Vector3 _position, _scale;
            private readonly Quaternion _rotation;
            internal RectState(RectTransform rect)
            {
                _min = rect.anchorMin; _max = rect.anchorMax; _pivot = rect.pivot; _size = rect.sizeDelta;
                _position = rect.anchoredPosition3D; _scale = rect.localScale; _rotation = rect.localRotation;
            }
            internal void Restore(RectTransform rect)
            {
                rect.anchorMin = _min; rect.anchorMax = _max; rect.pivot = _pivot; rect.sizeDelta = _size;
                rect.anchoredPosition3D = _position; rect.localScale = _scale; rect.localRotation = _rotation;
            }
        }
        private void OnDisable() => Release();
        private void OnDestroy() => Release();
    }
}
