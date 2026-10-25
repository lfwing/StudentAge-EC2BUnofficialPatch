using System;
using System.Collections.Generic;
using System.Linq;
using Config;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace EC2BUnofficialPatch.Features.Mechanics.MapExtend
{
    // Both the runtime label and its exported browser model use this sizing contract.
    internal static class MapLocationLabelLayout
    {
        internal static void Resize(RectTransform label, Vector2 size)
        {
            Vector2 delta = size - label.rect.size;
            RectTransform background = label.Find("img_name") as RectTransform;
            RectTransform text = label.Find("img_name/txt_name") as RectTransform;
            label.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size.x);
            label.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size.y);
            if (background.anchorMin == background.anchorMax) background.sizeDelta += delta;
            if (text.anchorMin == text.anchorMax) text.sizeDelta += delta;
            label.ForceUpdateRectTransforms();
        }

        internal static void Prepare(RectTransform label)
        {
            Transform roles = label.Find("itemgroup_role");
            foreach (ContentSizeFitter fitter in label.GetComponentsInChildren<ContentSizeFitter>(true))
                if (!fitter.transform.IsChildOf(roles)) fitter.enabled = false;
            foreach (AspectRatioFitter fitter in label.GetComponentsInChildren<AspectRatioFitter>(true))
                if (!fitter.transform.IsChildOf(roles)) fitter.enabled = false;
            foreach (LayoutGroup layout in label.GetComponentsInChildren<LayoutGroup>(true))
                if (!layout.transform.IsChildOf(roles)) layout.enabled = false;
            Text text = label.Find("img_name/txt_name").GetComponent<Text>();
            text.alignment = TextAnchor.MiddleCenter;
            text.resizeTextForBestFit = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = false;
        }

        internal static float TextWidth(RectTransform label, Text text, string value)
        {
            int rasterSize = text.font.dynamic ? Mathf.RoundToInt(text.fontSize * text.pixelsPerUnit) : 0;
            if (text.font.dynamic) text.font.RequestCharactersInTexture(value, rasterSize, text.fontStyle);
            float width = 0;
            foreach (char ch in value)
                if (text.font.GetCharacterInfo(ch, out CharacterInfo info, rasterSize, text.fontStyle)) width += info.advance;
            return width / text.pixelsPerUnit * label.InverseTransformVector(text.rectTransform.TransformVector(Vector3.right)).magnitude;
        }

        internal static float Padding(RectTransform label, Text text) =>
            Mathf.Max(24, label.rect.width - Frame(label, text.rectTransform)[2]);

        private static float[] Frame(RectTransform label, RectTransform rect)
        {
            Vector3 center = label.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
            Vector3 x = label.InverseTransformVector(rect.TransformVector(Vector3.right * rect.rect.width));
            Vector3 y = label.InverseTransformVector(rect.TransformVector(Vector3.up * rect.rect.height));
            return new[] { center.x, center.y, x.magnitude, y.magnitude };
        }

        private static float[] Difference(float[] a, float[] b) => a.Zip(b, (x, y) => (x - y) / 100).ToArray();

        private static Texture2D ReadTexture(Texture texture)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
            Texture2D copy = null;
            try
            {
                Graphics.Blit(texture, target);
                RenderTexture.active = target;
                copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                copy.Apply(false, false);
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
                RenderTexture.ReleaseTemporary(target);
            }
        }

        private static string Png(Texture2D texture) =>
            "data:image/png;base64," + Convert.ToBase64String(ImageConversion.EncodeToPNG(texture));

        // Capture the actual UI draw, including packed UVs, mesh effects and the
        // native material. Raw sprite pixels do not describe a custom UI shader.
        private static string ImagePng(Image image)
        {
            var mesh = new Mesh();
            Material material = null;
            Rect rect = image.rectTransform.rect;
            RenderTexture previous = RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(Mathf.Max(1, Mathf.RoundToInt(rect.width)),
                Mathf.Max(1, Mathf.RoundToInt(rect.height)), 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Texture2D copy = null;
            bool matrixPushed = false;
            try
            {
                using (var helper = new VertexHelper())
                {
                    typeof(Image).GetMethod("OnPopulateMesh", System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic, null, new[] { typeof(VertexHelper) }, null)
                        .Invoke(image, new object[] { helper });
                    foreach (BaseMeshEffect effect in image.GetComponents<BaseMeshEffect>())
                        if (effect.IsActive()) effect.ModifyMesh(helper);
                    helper.FillMesh(mesh);
                }
                material = new Material(image.materialForRendering);
                Color32[] colors = mesh.colors32;
                Color32 sourceColor = image.color;
                for (int i = 0; i < colors.Length; i++)
                    if (colors[i].Equals(sourceColor)) colors[i] = new Color32(255, 255, 255, 255);
                mesh.colors32 = colors;
                material.mainTexture = image.mainTexture;
                // Mask/stencil state belongs to the game Canvas, not this small target.
                material.DisableKeyword("UNITY_UI_CLIP_RECT");
                if (material.HasProperty("_StencilComp")) material.SetInt("_StencilComp", 8);
                if (material.HasProperty("_Stencil")) material.SetInt("_Stencil", 0);
                if (material.HasProperty("_StencilOp")) material.SetInt("_StencilOp", 0);
                if (material.HasProperty("_ColorMask")) material.SetInt("_ColorMask", 15);
                RenderTexture.active = target;
                GL.PushMatrix(); matrixPushed = true;
                GL.LoadPixelMatrix(rect.xMin, rect.xMax, rect.yMin, rect.yMax);
                copy = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
                GL.Clear(true, true, Color.black);
                if (!material.SetPass(0)) throw new InvalidOperationException("无法绘制原版标签材质：" + image.name);
                Graphics.DrawMeshNow(mesh, Matrix4x4.identity);
                copy.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                copy.Apply(false, false);
                Color32[] black = copy.GetPixels32();
                GL.Clear(true, true, Color.white);
                material.SetPass(0);
                Graphics.DrawMeshNow(mesh, Matrix4x4.identity);
                copy.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                copy.Apply(false, false);
                Color32[] white = copy.GetPixels32();
                // Recover straight alpha from black/white renders; UI blend modes
                // otherwise produce premultiplied RGB or squared alpha on a clear RT.
                for (int i = 0; i < black.Length; i++)
                {
                    float alpha = Mathf.Clamp01(1 - ((white[i].r - black[i].r) +
                        (white[i].g - black[i].g) + (white[i].b - black[i].b)) / 765f);
                    Color color = alpha > 0 ? new Color(black[i].r / (255f * alpha),
                        black[i].g / (255f * alpha), black[i].b / (255f * alpha), alpha) : Color.clear;
                    if (QualitySettings.activeColorSpace == ColorSpace.Linear) color = color.gamma;
                    black[i] = color;
                }
                copy.SetPixels32(black); copy.Apply(false, false);
                return Png(copy);
            }
            finally
            {
                if (matrixPushed) GL.PopMatrix();
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                if (copy != null) Object.Destroy(copy);
                Object.Destroy(material); Object.Destroy(mesh);
            }
        }

        internal static object Export(RectTransform template, IEnumerable<string> names)
        {
            if (template == null) return null;
            var container = new GameObject("EC2B_LabelLayout", typeof(RectTransform));
            container.SetActive(false);
            container.transform.SetParent(template.parent, false);
            var atlases = new Dictionary<Texture, Texture2D>();
            try
            {
                Image[] originals = template.GetComponentsInChildren<Image>(true);
                Sprite[] sprites = originals.Select(image => image.overrideSprite).ToArray();
                RectTransform label = Object.Instantiate(template, container.transform, false);
                Vector2 baseSize = template.rect.size;
                label.anchorMin = label.anchorMax = new Vector2(0.5f, 0.5f);
                label.pivot = new Vector2(0.5f, 0.5f);
                label.localScale = Vector3.one;
                label.localRotation = Quaternion.identity;
                label.anchoredPosition = Vector2.zero;
                label.sizeDelta = baseSize;
                Text text = label.Find("img_name/txt_name").GetComponent<Text>();
                Prepare(label);
                float padding = Padding(label, text);
                // Exclude NPCs and transient badges; these are not part of the label frame.
                Transform roles = label.Find("itemgroup_role");
                Image[] clonedImages = label.GetComponentsInChildren<Image>(true);
                // Capture active sprites from the displayed originals, before cloning
                // or resize adapters can substitute a serialized placeholder.
                Image[] images = clonedImages
                    .Where(image => sprites[Array.IndexOf(clonedImages, image)] != null && image.enabled && !image.transform.IsChildOf(roles) &&
                        !image.name.Contains("redpoint") && IsVisibleChild(image.transform, label)).ToArray();
                foreach (Image image in images)
                    image.overrideSprite = image.sprite = sprites[Array.IndexOf(clonedImages, image)];
                float[][] frames = images.Select(image => Frame(label, image.rectTransform)).ToArray();
                float[] textFrame = Frame(label, text.rectTransform);
                Resize(label, baseSize + new Vector2(100, 0));
                float[][] widths = images.Select((image, i) => Difference(Frame(label, image.rectTransform), frames[i])).ToArray();
                float[] textWidth = Difference(Frame(label, text.rectTransform), textFrame);
                Resize(label, baseSize + new Vector2(0, 100));
                float[][] heights = images.Select((image, i) => Difference(Frame(label, image.rectTransform), frames[i])).ToArray();
                float[] textHeight = Difference(Frame(label, text.rectTransform), textFrame);
                Resize(label, baseSize);
                var layers = images.Select((image, i) => new
                {
                    frame = frames[i], widthChange = widths[i], heightChange = heights[i],
                    tint = image.name == "img_name", color = ColorUtility.ToHtmlStringRGBA(image.color),
                    sliced = image.type == Image.Type.Sliced, preserveAspect = false,
                    pivot = new[] { image.rectTransform.pivot.x, image.rectTransform.pivot.y },
                    spriteSize = new[] { image.rectTransform.rect.width, image.rectTransform.rect.height },
                    padding = new float[4], paddingUnits = new float[4],
                    border = new[] { image.sprite.border.x / (image.pixelsPerUnit * image.pixelsPerUnitMultiplier),
                        image.sprite.border.y / (image.pixelsPerUnit * image.pixelsPerUnitMultiplier),
                        image.sprite.border.z / (image.pixelsPerUnit * image.pixelsPerUnitMultiplier),
                        image.sprite.border.w / (image.pixelsPerUnit * image.pixelsPerUnitMultiplier) },
                    borderUnits = new[] { image.sprite.border.x / (image.pixelsPerUnit * image.pixelsPerUnitMultiplier) * frames[i][2] / image.rectTransform.rect.width,
                        image.sprite.border.y / (image.pixelsPerUnit * image.pixelsPerUnitMultiplier) * frames[i][3] / image.rectTransform.rect.height,
                        image.sprite.border.z / (image.pixelsPerUnit * image.pixelsPerUnitMultiplier) * frames[i][2] / image.rectTransform.rect.width,
                        image.sprite.border.w / (image.pixelsPerUnit * image.pixelsPerUnitMultiplier) * frames[i][3] / image.rectTransform.rect.height },
                    png = ImagePng(originals[Array.IndexOf(clonedImages, image)])
                }).ToArray();
                string characters = new string((string.Concat(names.Where(name => name != null)) +
                    "大桥" + new string(Enumerable.Range(32, 95).Select(i => (char)i).ToArray())).Distinct().ToArray());
                int rasterSize = text.font.dynamic ? Mathf.RoundToInt(text.fontSize * text.pixelsPerUnit) : 0;
                if (text.font.dynamic) text.font.RequestCharactersInTexture(characters, rasterSize, text.fontStyle);
                text.text = "M";
                text.cachedTextGenerator.Populate("M", text.GetGenerationSettings(text.rectTransform.rect.size));
                text.font.GetCharacterInfo('M', out CharacterInfo reference, rasterSize, text.fontStyle);
                float baseline = text.cachedTextGenerator.verts.Take(4).Max(vertex => vertex.position.y) / text.pixelsPerUnit
                    - reference.maxY / text.pixelsPerUnit - text.rectTransform.rect.center.y;
                var glyphs = new Dictionary<string, object>();
                foreach (char ch in characters)
                {
                    if (!text.font.GetCharacterInfo(ch, out CharacterInfo info, rasterSize, text.fontStyle)) continue;
                    glyphs[ch.ToString()] = new
                    {
                        advance = info.advance, minX = info.minX, maxX = info.maxX, minY = info.minY, maxY = info.maxY,
                        uv = new[] { info.uvBottomLeft.x, info.uvBottomLeft.y, info.uvTopLeft.x, info.uvTopLeft.y,
                            info.uvTopRight.x, info.uvTopRight.y, info.uvBottomRight.x, info.uvBottomRight.y }
                    };
                }
                Texture atlas = text.font.material.mainTexture;
                return new
                {
                    rendering = "unity-image-alpha-v1", size = new[] { baseSize.x, baseSize.y }, padding, layers,
                    text = new { frame = textFrame, widthChange = textWidth, heightChange = textHeight,
                        fontSize = text.fontSize, unitsPerPixel = 1 / text.pixelsPerUnit, baseline,
                        scaleX = Frame(label, text.rectTransform)[2] / text.rectTransform.rect.width,
                        scaleY = Frame(label, text.rectTransform)[3] / text.rectTransform.rect.height, glyphs,
                        shadows = text.GetComponents<Shadow>().Where(effect => effect.enabled).Select(effect => new {
                            outline = effect is Outline, color = ColorUtility.ToHtmlStringRGBA(effect.effectColor),
                            distance = new[] { effect.effectDistance.x, effect.effectDistance.y } }).ToArray(),
                        atlas = Png(ReadAtlas(atlas, atlases)) }
                };
            }
            finally
            {
                foreach (Texture2D atlas in atlases.Values) Object.Destroy(atlas);
                Object.Destroy(container);
            }
        }

        private static Texture2D ReadAtlas(Texture texture, Dictionary<Texture, Texture2D> atlases)
        {
            if (!atlases.TryGetValue(texture, out Texture2D copy))
                atlases.Add(texture, copy = ReadTexture(texture));
            return copy;
        }

        private static bool IsVisibleChild(Transform child, Transform root)
        {
            for (Transform node = child; node != null && node != root; node = node.parent)
                if (!node.gameObject.activeSelf) return false;
            return true;
        }
    }
}
