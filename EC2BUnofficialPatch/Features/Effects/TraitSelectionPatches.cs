using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Bootstrap;
using GenUI.Common;
using HarmonyLib;
using Sdk;
using TheEntity;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using View.Common;
using View.Main;

namespace EC2BUnofficialPatch.Features.Effects
{
    internal static class TraitSelectionPatches
    {
        internal static void Install(Harmony harmony)
        {
            Type p = typeof(TraitSelectionPatches);
            EffectExtensionData.Patch(harmony, typeof(DetailSocialView), "SetTrait", p, prefix: nameof(SetSlots), postfix: nameof(Arrange));
            EffectExtensionData.Patch(harmony, typeof(DetailSocialView), "OnRenderTrait", p, postfix: nameof(Render));
            EffectExtensionData.Patch(harmony, typeof(RewardSelectView), "OnRender", p, prefix: nameof(RestoreCell));
            EffectExtensionData.Patch(harmony, typeof(RewardSelectView), "OnOpen", p, prefix: nameof(RestoreGroup));
        }

        internal static List<int> Slots(IList<int> traits)
        {
            int count = traits != null && traits.Count >= 4 ? 4 : 3;
            var result = traits?.Take(count).ToList() ?? new List<int>();
            while (result.Count < count) result.Add(-1);
            return result;
        }

        internal static bool SetSlots(UIItemGroup _ig, Role ___role)
        {
            if (HasOtherSlotProvider()) return true;
            _ig.SetDatas<int>(Slots(___role.Traits), null);
            return false;
        }

        internal static bool HasOtherSlotProvider() => Chainloader.PluginInfos.ContainsKey("com.lyy.traits4") ||
            (Harmony.GetPatchInfo(AccessTools.Method(typeof(DetailSocialView), "SetTrait"))?.Owners.Contains("com.lyy.traits4") ?? false);

        internal static void Arrange(UIItemGroup _ig)
        {
            var layout = _ig.transform.GetComponent<TraitGroupLayout>() ?? _ig.transform.gameObject.AddComponent<TraitGroupLayout>();
            layout.Configure(_ig.GetCells());
        }

        internal static void Render(Cell_RewardSelectItemUI _cell)
        {
            var layout = _cell.gameObject.GetComponent<TraitCardContent>() ?? _cell.gameObject.AddComponent<TraitCardContent>();
            layout.Configure(_cell);
        }

        internal static void RestoreCell(UICell _cell) => _cell.gameObject.GetComponent<TraitCardContent>()?.Restore();
        internal static void RestoreGroup(RewardSelectView __instance) => __instance.itemgroup_data?.transform.GetComponent<TraitGroupLayout>()?.Restore();

        internal static float FitScale(float width, float height, float cardWidth, float cardHeight, int count)
        {
            if (width <= 0 || height <= 0 || cardWidth <= 0 || cardHeight <= 0 || count <= 0) return 1;
            return Math.Min(1, Math.Min(width / (cardWidth * count * 1.04f), height / cardHeight));
        }
    }

    internal sealed class TraitRectState
    {
        private readonly RectTransform rect;
        private readonly Vector2 anchorMin, anchorMax, pivot, size, position;
        private readonly Vector3 scale;
        internal TraitRectState(RectTransform value)
        {
            rect = value; anchorMin = rect.anchorMin; anchorMax = rect.anchorMax; pivot = rect.pivot;
            size = rect.sizeDelta; position = rect.anchoredPosition; scale = rect.localScale;
        }
        internal void Restore()
        {
            if (rect == null) return;
            rect.anchorMin = anchorMin; rect.anchorMax = anchorMax; rect.pivot = pivot;
            rect.sizeDelta = size; rect.anchoredPosition = position; rect.localScale = scale;
        }
    }

    internal sealed class TraitGroupLayout : MonoBehaviour
    {
        private readonly List<TraitRectState> saved = new List<TraitRectState>();
        private List<UICell> cells;
        private LayoutGroup layout;
        private bool layoutWasEnabled;
        private Vector2 measured;
        private bool pending;

        internal void Configure(List<UICell> value)
        {
            Restore();
            cells = value.ToList();
            layout = GetComponent<LayoutGroup>();
            layoutWasEnabled = layout != null && layout.enabled;
            foreach (var cell in cells) saved.Add(new TraitRectState(cell.transform));
            pending = true;
        }

        private void LateUpdate()
        {
            var rect = transform as RectTransform;
            if (cells == null || rect == null || (!pending && measured == rect.rect.size)) return;
            measured = rect.rect.size; pending = false;
            if (layout != null) layout.enabled = false;
            float width = 0, height = 0;
            foreach (var cell in cells)
            {
                float angle = cell.transform.localEulerAngles.z * Mathf.Deg2Rad;
                float cos = Mathf.Abs(Mathf.Cos(angle)), sin = Mathf.Abs(Mathf.Sin(angle));
                width = Math.Max(width, cos * cell.transform.rect.width + sin * cell.transform.rect.height);
                height = Math.Max(height, sin * cell.transform.rect.width + cos * cell.transform.rect.height);
            }
            float scale = TraitSelectionPatches.FitScale(measured.x * .96f, measured.y * .96f, width, height, cells.Count);
            for (int i = 0; i < cells.Count; i++)
            {
                var card = cells[i].transform;
                card.anchorMin = card.anchorMax = new Vector2(.5f, .5f);
                card.pivot = new Vector2(.5f, .5f);
                card.localScale = Vector3.one * scale;
                card.anchoredPosition = new Vector2((i - (cells.Count - 1) * .5f) * width * scale * 1.04f, 0);
            }
        }

        internal void Restore()
        {
            foreach (var state in saved) state.Restore();
            saved.Clear();
            if (layout != null) layout.enabled = layoutWasEnabled;
            cells = null; pending = false;
        }
        private void OnDisable() => Restore();
    }

    internal sealed class TraitCardContent : MonoBehaviour
    {
        private Cell_RewardSelectItemUI cell;
        private TraitRectState rect;
        private float fontSize, fontMin, fontMax;
        private bool autosize, wrapping, raycast;
        private Vector4 margin;
        private TextOverflowModes overflow;
        private GameObject more;
        private TraitDescriptionView full;
        private int pending;
        private Vector2 measured;

        internal void Configure(Cell_RewardSelectItemUI value)
        {
            Restore();
            cell = value;
            var text = cell.txtex_effect;
            rect = new TraitRectState(text.rectTransform);
            fontSize = text.fontSize; fontMin = text.fontSizeMin; fontMax = text.fontSizeMax;
            autosize = text.enableAutoSizing; wrapping = text.enableWordWrapping;
            margin = text.margin; overflow = text.overflowMode; raycast = text.raycastTarget;
            pending = 2;
        }

        private void LateUpdate()
        {
            if (cell == null) return;
            if (pending > 0 && --pending > 0) return;
            if (pending == 0 && measured == cell.transform.rect.size) return;
            pending = 0; measured = cell.transform.rect.size;
            Fit();
        }

        private void Fit()
        {
            if (cell == null || cell.txtex_effect == null || cell.txtex_title == null) return;
            var text = cell.txtex_effect;
            var parent = text.rectTransform.parent as RectTransform;
            if (parent == null) return;
            rect.Restore();
            float padding = Math.Max(4, fontSize * .35f);
            // Title and body bounds are measured in the actual prefab parent's space.
            var corners = new Vector3[4];
            cell.txtex_title.rectTransform.GetWorldCorners(corners);
            float titleBottom = corners.Min(c => parent.InverseTransformPoint(c).y);
            text.rectTransform.GetWorldCorners(corners);
            float originalTop = corners.Max(c => parent.InverseTransformPoint(c).y);
            float top = Math.Min(originalTop, Math.Min(parent.rect.yMax - padding, titleBottom - padding));
            float bottom = parent.rect.yMin + padding;
            float available = Math.Max(fontSize, top - bottom);
            var body = text.rectTransform;
            body.anchorMin = Vector2.zero; body.anchorMax = Vector2.one;
            body.offsetMin = new Vector2(padding, padding);
            body.offsetMax = new Vector2(-padding, top - parent.rect.yMax);
            text.margin = Vector4.zero;
            text.enableWordWrapping = true; text.raycastTarget = false;
            text.enableAutoSizing = true; text.fontSizeMax = fontSize;
            text.fontSizeMin = Math.Min(fontSize, Math.Max(14, fontSize * .65f));
            text.overflowMode = TextOverflowModes.Overflow;
            Vector2 required = text.GetPreferredValues(text.text, Math.Max(1, body.rect.width), float.PositiveInfinity);
            bool needsFull = required.y > available;
            if (more != null) { more.SetActive(false); Destroy(more); more = null; }
            if (needsFull)
            {
                float footer = fontSize * 1.5f;
                body.offsetMin = new Vector2(padding, padding + footer);
                text.overflowMode = TextOverflowModes.Ellipsis;
                more = Button(parent, text, "查看完整说明", ShowFull);
                var link = (RectTransform)more.transform;
                link.anchorMin = Vector2.zero; link.anchorMax = new Vector2(1, 0);
                link.pivot = new Vector2(.5f, 0);
                link.offsetMin = new Vector2(padding, padding);
                link.offsetMax = new Vector2(-padding, padding + footer);
            }
            text.ForceMeshUpdate();
        }

        private static GameObject Object(string name, Transform parent, params Type[] components)
        {
            var value = new GameObject(name, components);
            value.transform.SetParent(parent, false);
            return value;
        }

        private static TextMeshProUGUI Text(Transform parent, TextMeshProUGUI source, string content)
        {
            var value = Object("Text", parent, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            var text = value.GetComponent<TextMeshProUGUI>();
            text.font = source.font; text.fontSharedMaterial = source.fontSharedMaterial;
            text.fontSize = source.fontSize; text.color = source.color;
            text.text = content; text.enableWordWrapping = true; text.raycastTarget = false;
            var r = text.rectTransform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
            return text;
        }

        private static GameObject Button(Transform parent, TextMeshProUGUI source, string label, UnityEngine.Events.UnityAction clicked)
        {
            var value = Object("UP.TraitFullDescription", parent, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            value.GetComponent<Image>().color = new Color(1, 1, 1, .08f);
            value.GetComponent<Button>().onClick.AddListener(clicked);
            var text = Text(value.transform, source, label);
            text.fontSize = Math.Min(source.fontSize, source.fontSizeMax);
            text.alignment = TextAlignmentOptions.Center;
            return value;
        }

        private void ShowFull()
        {
            if (cell == null || (full != null && full.Owner == this)) return;
            full = TraitDescriptionView.Show(this, cell.txtex_title.text, cell.txtex_effect.text);
        }

        private void CloseFull()
        {
            if (full != null && full.Owner == this) full.CloseView();
            full = null;
        }

        internal void Restore()
        {
            if (cell != null && cell.txtex_effect != null)
            {
                var text = cell.txtex_effect;
                rect?.Restore(); text.enableAutoSizing = autosize; text.fontSize = fontSize;
                text.fontSizeMin = fontMin; text.fontSizeMax = fontMax;
                text.enableWordWrapping = wrapping; text.margin = margin;
                text.overflowMode = overflow; text.raycastTarget = raycast;
            }
            if (more != null) { more.SetActive(false); Destroy(more); more = null; }
            CloseFull(); cell = null; rect = null; pending = 0;
        }
        private void OnDisable() => Restore();
    }
}
