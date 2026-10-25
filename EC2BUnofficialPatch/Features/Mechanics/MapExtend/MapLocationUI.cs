using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using Config;
using EC2BUnofficialPatch.Core;
using EC2BUnofficialPatch.Resources;
using GenUI.Main;
using Newtonsoft.Json;
using Sdk;
using Sdk.Components;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using View.Main;
using Object = UnityEngine.Object;

namespace EC2BUnofficialPatch.Features.Mechanics.MapExtend
{
    // The generated roots deliberately match Cell_MapItemUI. MapView owns their
    // click routing, NPC icons, refresh and destruction just like native locations.
    internal sealed class MapLocationsSession : MonoBehaviour
    {
        internal readonly List<MapLocationMarker> Markers = new List<MapLocationMarker>();
        private readonly List<MapNativeHitFilter> _nativeFilters = new List<MapNativeHitFilter>();
        private readonly List<Object> _generatedImages = new List<Object>();
        private TextureCache _textures;
        private bool _checkingNative;

        internal void Build(MapView view, MapLocationRegistry registry, RectTransform labelTemplate = null)
        {
            Release();
            _textures = new TextureCache();
            RectTransform group = view.group_item;
            Transform[] natives = Enumerable.Range(0, group.childCount).Select(group.GetChild).ToArray();
            // Clone the complete native capsule, including its border, arrow and shadow.
            RectTransform nativeLabel = labelTemplate ?? natives.Select(node => node.Find("btn_name") as RectTransform)
                .Where(node => node != null && node.rect.width > 0 && node.rect.height > 0 && node.Find("img_name/txt_name") != null)
                .OrderByDescending(node => node.rect.width / Mathf.Max(1, node.rect.height)).FirstOrDefault();
            ExportLayout(group, natives, nativeLabel, registry);
            var existingIds = new HashSet<int>();
            foreach (Transform native in natives)
            {
                if (native.name.StartsWith("_pos_", StringComparison.Ordinal) &&
                    int.TryParse(native.name.Substring(5), out int id))
                {
                    existingIds.Add(id);
                }
            }

            var claimedIds = new HashSet<int>();
            foreach (MapLocationEntry entry in registry.Entries.Where(item => item.LocalContent || (item.ModId != 0 &&
                Singleton<ModCtrl>.Ins.activeMods != null && Singleton<ModCtrl>.Ins.activeMods.Contains(item.ModId)))
                .OrderBy(item => item.LocalContent)
                .Where(item => claimedIds.Add(item.MapId)).OrderBy(item => item.Priority).ThenBy(item => item.MapId))
            {
                // ContentRootCatalog includes subscriptions; only enabled game Mods can add locations.
                if (!entry.LocalContent && (entry.ModId == 0 || Singleton<ModCtrl>.Ins.activeMods == null ||
                    !Singleton<ModCtrl>.Ins.activeMods.Contains(entry.ModId))) continue;
                if (!Cfg.MapCfgMap.TryGetValue(entry.MapId, out MapCfg cfg) || cfg.id != entry.MapId || cfg.type == 1 ||
                    cfg.bg <= 0 || !Cfg.BgCfgMap.ContainsKey(cfg.bg) ||
                    (cfg.bg2 > 0 && !Cfg.BgCfgMap.ContainsKey(cfg.bg2)) || existingIds.Contains(entry.MapId))
                {
                    PatchLog.Warning($"机制模块-地图扩展与优化跳过 mapId={entry.MapId}：地点/背景缺失、配置ID不一致、type=1 或已有大地图入口；source={entry.Source}");
                    continue;
                }
                MapLocationMarker marker = null;
                try
                {
                    if (cfg.floors == null) cfg.floors = new List<int>();
                    // Parse conditions before handing the node to the unguarded native renderer.
                    CommonEvtMgr.IsMatchCondition(cfg.cond, true);
                    marker = Create(group, entry, nativeLabel);
                    Markers.Add(marker);
                    existingIds.Add(entry.MapId);
                }
                catch (Exception exception)
                {
                    if (marker != null) Remove(marker);
                    PatchLog.Warning($"机制模块-地图扩展与优化创建失败 mapId={entry.MapId}：{ModuleHost.GetReason(exception)}");
                }
            }
            if (Markers.Count == 0) return;
            // Native priority is 2. Native ties retain prefab order; custom ties
            // follow natives, then ascending mapId. Input uses this same order.
            var ordered = natives.Select((node, index) => new { Node = node, Priority = 2, Custom = false, Order = index })
                .Concat(Markers.Select(marker => new { Node = marker.transform, Priority = marker.Entry.Priority, Custom = true, Order = marker.Entry.MapId }))
                .OrderBy(item => item.Priority).ThenBy(item => item.Custom).ThenBy(item => item.Order);
            foreach (var item in ordered) item.Node.SetAsLastSibling();
            foreach (Transform native in natives)
            {
                MapNativeHitFilter filter = native.gameObject.AddComponent<MapNativeHitFilter>();
                filter.Session = this;
                _nativeFilters.Add(filter);
            }
        }

        private MapLocationMarker Create(RectTransform parent, MapLocationEntry entry, RectTransform nativeLabel)
        {
            RectTransform root = Rect("_pos_" + entry.MapId, parent, entry.Position, entry.Size);
            // Keep cloned source interactions inactive until they have been detached.
            root.gameObject.SetActive(false);
            MapLocationMarker marker = root.gameObject.AddComponent<MapLocationMarker>();
            marker.Entry = entry;
            marker.Session = this;
            try
            {
                root.gameObject.AddComponent<MapCustomHitFilter>().Marker = marker;
                RectTransform body = Rect("btn_click", root, Vector2.zero, entry.Size);
                MapLocationHitImage hit = body.gameObject.AddComponent<MapLocationHitImage>();
                hit.Marker = marker;
                hit.raycastTarget = true;
                hit.color = Color.clear;
                hit.canvasRenderer.cullTransparentMesh = false;
                marker.Hit = hit;
                AddButton(body, hit);

                Image normal = CreateVisual("normal", root, entry.ImagePath, entry.ImagePosition, entry.ImageSize, entry);
                marker.Normal = normal == null ? null : normal.gameObject;
                if (entry.Polygon == null)
                {
                    if (normal == null) throw new InvalidOperationException("alpha 命中需要有效的普通 PNG。");
                    hit.sprite = normal.sprite;
                }
                Image hover = CreateVisual("hover", root, entry.HoverImagePath, entry.HoverPosition, entry.HoverSize, entry);
                marker.ReplacesNormal = hover != null;
                if (hover != null) marker.Hover = hover.gameObject;
                else if (entry.Polygon != null)
                {
                    RectTransform outlineRect = Rect("hover", root, Vector2.zero, entry.Size);
                    MapLocationOutline outline = outlineRect.gameObject.AddComponent<MapLocationOutline>();
                    outline.Points = entry.Polygon;
                    outline.Width = entry.OutlineWidth;
                    outline.color = entry.OutlineColor;
                    outline.raycastTarget = false;
                    outline.SetVerticesDirty();
                    marker.Hover = outlineRect.gameObject;
                }
                else
                {
                    RectTransform outlineRect = Rect("hover", root, Vector2.zero, entry.Size);
                    Image outline = outlineRect.gameObject.AddComponent<Image>();
                    outline.sprite = MakeAlphaOutline(normal.sprite.texture, entry);
                    outline.color = entry.OutlineColor;
                    outline.raycastTarget = false;
                    outlineRect.sizeDelta = new Vector2(
                        entry.Size.x * outline.sprite.texture.width / normal.sprite.texture.width,
                        entry.Size.y * outline.sprite.texture.height / normal.sprite.texture.height);
                    marker.Hover = outlineRect.gameObject;
                }
                marker.Hover.SetActive(false);
                CreateLabel(root, marker, nativeLabel);
                return marker;
            }
            catch { Remove(marker); throw; }
        }

        private Image CreateVisual(string name, RectTransform root, string path, Vector2 position, Vector2 size, MapLocationEntry entry)
        {
            if (path == null) return null;
            if (!_textures.TryGetSprite(path, out Sprite sprite))
            {
                PatchLog.Warning($"机制模块-大地图图片读取失败 mapId={entry.MapId}, layer={name}, path={path}；保留命中区域并使用后备描边。");
                return null;
            }
            RectTransform rect = Rect(name, root, position - entry.Position, size);
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = Color.white;
            image.raycastTarget = false;
            return image;
        }

        private static void CreateLabel(RectTransform root, MapLocationMarker marker, RectTransform template)
        {
            if (template == null) throw new InvalidOperationException("未找到原版普通地点标签。");
            RectTransform label = Object.Instantiate(template, root, false);
            label.name = "btn_name";
            label.gameObject.SetActive(false);
            // Preserve the native baseline size before rebasing anchors onto the new hit root.
            label.anchorMin = label.anchorMax = root.pivot;
            label.pivot = new Vector2(0.5f, 0.5f);
            label.sizeDelta = template.rect.size;
            label.localRotation = Quaternion.identity;
            label.localScale = Vector3.one;
            // Native callbacks/effects can reference the source building outside the cloned label.
            foreach (ColorInteract interaction in label.GetComponentsInChildren<ColorInteract>(true))
            { interaction.enabled = false; Object.Destroy(interaction); }
            foreach (EventTrigger trigger in label.GetComponentsInChildren<EventTrigger>(true))
            { trigger.enabled = false; Object.Destroy(trigger); }
            foreach (Button button in label.GetComponentsInChildren<Button>(true))
            {
                button.onClick = new Button.ButtonClickedEvent();
                button.transition = Selectable.Transition.None;
                button.navigation = new Navigation { mode = Navigation.Mode.None };
            }
            foreach (Graphic graphic in label.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            Image background = label.Find("img_name").GetComponent<Image>();
            Text text = label.Find("img_name/txt_name").GetComponent<Text>();
            MapLocationEntry entry = marker.Entry;
            MapLocationLabelLayout.Prepare(label);
            text.text = entry.Label ?? Cfg.MapCfgMap[entry.MapId].name;
            text.color = entry.TextColor;
            background.color = entry.LabelColor;
            background.raycastTarget = true;
            background.canvasRenderer.cullTransparentMesh = false;
            if (label.GetComponent<Button>() == null) AddButton(label, background);
            Vector2 size = entry.LabelSize;
            if (size == Vector2.zero)
            {
                float padding = MapLocationLabelLayout.Padding(label, text);
                size = new Vector2(Mathf.Max(template.rect.width, MapLocationLabelLayout.TextWidth(label, text, text.text) + padding), template.rect.height);
            }
            label.anchorMin = label.anchorMax = root.pivot;
            label.pivot = new Vector2(0.5f, 0.5f);
            label.anchoredPosition = entry.LabelPosition - entry.Position;
            label.localRotation = Quaternion.identity;
            label.localScale = Vector3.one * entry.LabelScale;
            MapLocationLabelLayout.Resize(label, size);
            Transform roles = label.Find("itemgroup_role");
            if (roles == null) throw new InvalidOperationException("原版标签缺少 itemgroup_role。");
            foreach (Transform child in roles) child.gameObject.SetActive(false);
            marker.LabelRect = label;
            label.gameObject.SetActive(entry.ShowLabel);
        }

        internal bool IsTop(MapLocationMarker current, Vector2 point, Camera camera)
        {
            if (_checkingNative) return current == null;
            MapLocationMarker winner = null;
            int priority = 0;
            _checkingNative = true;
            try
            {
                foreach (MapNativeHitFilter filter in _nativeFilters)
                    if (filter != null && filter.gameObject.activeInHierarchy &&
                        filter.GetComponentsInChildren<Graphic>(false).Any(graphic => graphic.enabled && graphic.raycastTarget &&
                            RectTransformUtility.RectangleContainsScreenPoint(graphic.rectTransform, point, camera) && graphic.Raycast(point, camera)))
                        priority = 2;
            }
            finally { _checkingNative = false; }
            foreach (MapLocationMarker marker in Markers)
                if (marker != null && marker.gameObject.activeInHierarchy && marker.Entry.Priority >= priority && marker.Covers(point, camera))
                {
                    winner = marker; priority = marker.Entry.Priority;
                }
            return winner == current;
        }

        internal void RefreshVisibility()
        {
            foreach (MapLocationMarker marker in Markers)
                if (marker != null) marker.gameObject.SetActive(IsEligible(marker.Entry.MapId));
        }

        private static bool IsEligible(int id)
        {
            if (!Cfg.MapCfgMap.TryGetValue(id, out MapCfg cfg)) return false;
            List<int> npcs = Singleton<FuncMgr>.Ins.GetMapData().GetNpcs(id, true);
            return (Singleton<FuncMgr>.Ins.GetMapData().IsUnlockMapScene(id, true) &&
                CommonEvtMgr.IsMatchCondition(cfg.cond, true)) || (npcs != null && npcs.Count > 0);
        }

        internal void Release()
        {
            foreach (MapNativeHitFilter filter in _nativeFilters)
                if (filter != null) { filter.Session = null; Object.Destroy(filter); }
            _nativeFilters.Clear();
            foreach (MapLocationMarker marker in Markers) if (marker != null) Remove(marker);
            Markers.Clear();
            _textures?.Dispose();
            _textures = null;
            foreach (Object image in _generatedImages) if (image != null) Object.Destroy(image);
            _generatedImages.Clear();
        }

        internal void HideAll()
        {
            foreach (MapLocationMarker marker in Markers)
                if (marker != null) marker.gameObject.SetActive(false);
        }

        private void OnDestroy() => Release();

        private static void Remove(MapLocationMarker marker)
        {
            marker.gameObject.SetActive(false);
            // Destroy is deferred; detach immediately so MapView.OnOpen cannot parse it.
            marker.transform.SetParent(null, false);
            Object.Destroy(marker.gameObject);
        }

        private static RectTransform Rect(string name, RectTransform parent, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = parent.pivot;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static void AddButton(RectTransform rect, Image image)
        {
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.normalColor = colors.highlightedColor = colors.pressedColor = colors.selectedColor = Color.white;
            button.colors = colors;
        }

        private static void ExportLayout(RectTransform group, Transform[] natives, RectTransform template, MapLocationRegistry registry)
        {
            try
            {
                Canvas canvas = group.GetComponentInParent<Canvas>();
                Camera camera = canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay
                    ? null : canvas?.worldCamera ?? UIMgr.Camera;
                Vector2 origin = RectTransformUtility.WorldToScreenPoint(camera, group.TransformPoint(Vector3.zero));
                Vector2 x = RectTransformUtility.WorldToScreenPoint(camera, group.TransformPoint(Vector3.right)) - origin;
                Vector2 y = RectTransformUtility.WorldToScreenPoint(camera, group.TransformPoint(Vector3.up)) - origin;
                string path = Path.Combine(Paths.PluginPath, "EC2BUnofficialPatch", "MapExtend", "MapLayout.json");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonConvert.SerializeObject(new
                {
                    schema = 2, screenWidth = Screen.width, screenHeight = Screen.height,
                    worldId = WorldMapsRuntime.ActiveWorldId,
                    origin = new[] { origin.x, Screen.height - origin.y },
                    labelStyle = MapLocationLabelLayout.Export(template, Cfg.MapCfgMap.Values.Select(cfg => cfg.name)
                        .Concat(registry.Entries.Select(entry => entry.Label))),
                    background = ExportBackground(group),
                    basisX = new[] { x.x, -x.y }, basisY = new[] { y.x, -y.y },
                    nativeLocations = natives.OfType<RectTransform>().Select(rect => new
                    {
                        node = rect.name, priority = 2,
                        position = new[] { rect.localPosition.x, rect.localPosition.y }
                    }).ToArray()
                }, Formatting.Indented));
                if (WorldMapsRuntime.ActiveWorldId != 0)
                    File.Copy(path, Path.Combine(Path.GetDirectoryName(path), "MapLayout-" + WorldMapsRuntime.ActiveWorldId + ".json"), true);
            }
            catch (Exception exception)
            {
                PatchLog.Warning("机制模块-大地图坐标辅助文件导出失败，不影响地点显示：" + ModuleHost.GetReason(exception));
            }
        }

        private static object ExportBackground(RectTransform group)
        {
            var candidates = Object.FindObjectsOfType<Image>().Where(image => image.sprite != null && image.enabled &&
                image.gameObject.activeInHierarchy && (!image.transform.IsChildOf(group) || image.transform == group) && image.type == Image.Type.Simple &&
                image.sprite.rect.width >= 512 && image.sprite.rect.height >= 256);
            Image background = group.GetComponentInParent<WorldMapSession>()?.BackgroundImage ?? candidates.OrderByDescending(image => image.transform.IsChildOf(group.parent))
                .ThenByDescending(image => image.rectTransform.rect.width * image.rectTransform.rect.height).FirstOrDefault();
            if (background == null)
            {
                RawImage raw = Object.FindObjectsOfType<RawImage>().Where(image => image.texture != null && image.enabled &&
                    image.gameObject.activeInHierarchy && image.texture.width >= 512 && image.texture.height >= 256)
                    .OrderByDescending(image => image.transform.IsChildOf(group.parent))
                    .ThenByDescending(image => image.rectTransform.rect.width * image.rectTransform.rect.height).FirstOrDefault();
                if (raw == null) return null;
                Rect area = raw.rectTransform.rect, uv = raw.uvRect;
                if (Mathf.Abs(uv.width) < 0.0001f || Mathf.Abs(uv.height) < 0.0001f) return null;
                Canvas rawCanvas = raw.GetComponentInParent<Canvas>();
                Camera rawCamera = rawCanvas != null && rawCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rawCanvas?.worldCamera ?? UIMgr.Camera;
                Func<float, float, Vector2> rawProject = (u, v) => {
                    Vector3 point = new Vector3(area.xMin + (u - uv.x) / uv.width * area.width,
                        area.yMin + (v - uv.y) / uv.height * area.height, 0);
                    Vector2 screen = RectTransformUtility.WorldToScreenPoint(rawCamera, raw.rectTransform.TransformPoint(point));
                    return new Vector2(screen.x, Screen.height - screen.y);
                };
                Vector2 rawOrigin = rawProject(0, 1), rawX = rawProject(1, 1) - rawOrigin, rawY = rawProject(0, 0) - rawOrigin;
                return new { sprite = raw.texture.name, width = raw.texture.width, height = raw.texture.height,
                    origin = new[] { rawOrigin.x, rawOrigin.y }, axisX = new[] { rawX.x, rawX.y }, axisY = new[] { rawY.x, rawY.y } };
            }
            Rect rect = background.rectTransform.rect;
            if (background.preserveAspect)
            {
                float aspect = background.sprite.rect.width / background.sprite.rect.height;
                if (rect.width / rect.height > aspect)
                { float width = rect.height * aspect; rect.x += (rect.width - width) * background.rectTransform.pivot.x; rect.width = width; }
                else
                { float height = rect.width / aspect; rect.y += (rect.height - height) * background.rectTransform.pivot.y; rect.height = height; }
            }
            Canvas canvas = background.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas?.worldCamera ?? UIMgr.Camera;
            Func<Vector2, Vector2> project = point => {
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(camera, background.rectTransform.TransformPoint(point));
                return new Vector2(screen.x, Screen.height - screen.y);
            };
            Vector2 origin = project(new Vector2(rect.xMin, rect.yMax));
            Vector2 x = project(new Vector2(rect.xMax, rect.yMax)) - origin;
            Vector2 y = project(new Vector2(rect.xMin, rect.yMin)) - origin;
            return new { sprite = background.sprite.name, width = background.sprite.rect.width, height = background.sprite.rect.height,
                origin = new[] { origin.x, origin.y }, axisX = new[] { x.x, x.y }, axisY = new[] { y.x, y.y } };
        }

        private Sprite MakeAlphaOutline(Texture2D source, MapLocationEntry entry)
        {
            // Two-pass distance transform; handles holes and disconnected silhouettes.
            int padding = Mathf.Clamp(Mathf.CeilToInt(entry.OutlineWidth *
                Mathf.Max(source.width / entry.Size.x, source.height / entry.Size.y)), 1, 64);
            int width = source.width + padding * 2, height = source.height + padding * 2;
            Color32[] input = source.GetPixels32();
            var distance = new float[width * height];
            var opaque = new bool[distance.Length];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                opaque[index] = x >= padding && y >= padding && x < width - padding && y < height - padding &&
                    input[(y - padding) * source.width + x - padding].a / 255f >= entry.AlphaThreshold;
                distance[index] = opaque[index] ? 0 : width + height;
            }
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                if (x > 0) distance[i] = Mathf.Min(distance[i], distance[i - 1] + 1);
                if (y > 0) distance[i] = Mathf.Min(distance[i], distance[i - width] + 1);
                if (x > 0 && y > 0) distance[i] = Mathf.Min(distance[i], distance[i - width - 1] + 1.414214f);
                if (x + 1 < width && y > 0) distance[i] = Mathf.Min(distance[i], distance[i - width + 1] + 1.414214f);
            }
            for (int y = height - 1; y >= 0; y--) for (int x = width - 1; x >= 0; x--)
            {
                int i = y * width + x;
                if (x + 1 < width) distance[i] = Mathf.Min(distance[i], distance[i + 1] + 1);
                if (y + 1 < height) distance[i] = Mathf.Min(distance[i], distance[i + width] + 1);
                if (x + 1 < width && y + 1 < height) distance[i] = Mathf.Min(distance[i], distance[i + width + 1] + 1.414214f);
                if (x > 0 && y + 1 < height) distance[i] = Mathf.Min(distance[i], distance[i + width - 1] + 1.414214f);
            }
            var pixels = new Color32[distance.Length];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(255, 255, 255, (byte)(!opaque[i] && distance[i] <= padding ? 255 : 0));
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            _generatedImages.Add(texture);
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f));
            _generatedImages.Add(sprite);
            return sprite;
        }
    }

    internal sealed class MapLocationMarker : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        internal MapLocationEntry Entry;
        internal MapLocationsSession Session;
        internal MapLocationHitImage Hit;
        internal RectTransform LabelRect;
        internal GameObject Hover;
        internal GameObject Normal;
        internal bool ReplacesNormal;

        internal bool Covers(Vector2 screen, Camera camera)
        {
            if (Hit != null && Hit.ContainsScreen(screen, camera)) return true;
            return Entry.ShowLabel && LabelRect != null && LabelRect.gameObject.activeInHierarchy &&
                RectTransformUtility.RectangleContainsScreenPoint(LabelRect, screen, camera);
        }

        private void SetHover(bool active)
        {
            if (Hover != null) Hover.SetActive(active);
            if (Normal != null && ReplacesNormal) Normal.SetActive(!active);
        }
        public void OnPointerEnter(PointerEventData data) => SetHover(true);
        public void OnPointerExit(PointerEventData data) => SetHover(false);
        private void OnDisable() => SetHover(false);
    }

    internal sealed class MapCustomHitFilter : MonoBehaviour, ICanvasRaycastFilter
    {
        internal MapLocationMarker Marker;
        public bool IsRaycastLocationValid(Vector2 point, Camera camera) =>
            Marker == null || Marker.Session == null || Marker.Session.IsTop(Marker, point, camera);
    }

    internal sealed class MapNativeHitFilter : MonoBehaviour, ICanvasRaycastFilter
    {
        internal MapLocationsSession Session;
        public bool IsRaycastLocationValid(Vector2 point, Camera camera) =>
            Session == null || Session.IsTop(null, point, camera);
    }

    internal sealed class MapLocationHitImage : Image
    {
        internal MapLocationMarker Marker;
        internal bool ContainsScreen(Vector2 screen, Camera camera)
        {
            if (!RectTransformUtility.RectangleContainsScreenPoint(rectTransform, screen, camera) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screen, camera, out Vector2 point))
                return false;
            if (Marker.Entry.Polygon != null) return MapLocationGeometry.Contains(Marker.Entry.Polygon, point);
            if (sprite == null) return false;
            Rect rect = rectTransform.rect;
            float u = (point.x - rect.xMin) / rect.width, v = (point.y - rect.yMin) / rect.height;
            Texture2D texture = sprite.texture;
            int x = Mathf.Clamp((int)(u * texture.width), 0, texture.width - 1);
            int y = Mathf.Clamp((int)(v * texture.height), 0, texture.height - 1);
            return texture.GetPixel(x, y).a >= Marker.Entry.AlphaThreshold;
        }

        public override bool IsRaycastLocationValid(Vector2 point, Camera camera) =>
            Marker != null && ContainsScreen(point, camera);
    }

    internal sealed class MapLocationOutline : MaskableGraphic
    {
        internal Vector2[] Points;
        internal float Width;
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (Points == null) return;
            for (int i = 0; i < Points.Length; i++)
            {
                Vector2 a = Points[i], b = Points[(i + 1) % Points.Length];
                Vector2 direction = (b - a).normalized;
                Vector2 normal = new Vector2(-direction.y, direction.x) * (Width * 0.5f);
                Quad(mesh, a + normal, b + normal, b - normal, a - normal);
                Vector2 half = Vector2.one * (Width * 0.5f);
                Quad(mesh, a - half, a + new Vector2(half.x, -half.y), a + half, a + new Vector2(-half.x, half.y));
            }
        }

        private void Quad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            int start = mesh.currentVertCount;
            mesh.AddVert(a, color, Vector2.zero); mesh.AddVert(b, color, Vector2.zero);
            mesh.AddVert(c, color, Vector2.zero); mesh.AddVert(d, color, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
