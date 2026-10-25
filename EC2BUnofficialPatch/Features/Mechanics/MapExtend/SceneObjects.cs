using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Config;
using EC2BUnofficialPatch.Core;
using EC2BUnofficialPatch.Resources;
using EC2BUnofficialPatch.Workshop;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Sdk;
using UnityEngine;
using UnityEngine.UI;
using View.Main;

namespace EC2BUnofficialPatch.Features.Mechanics.MapExtend
{
    internal sealed class SceneObjectEntry
    {
        internal string Key, Background, Url, Stage, Visibility, Click;
        internal int MapId, PersonId, Target, BackgroundId;
        internal string Name;
        internal bool Modern;
        internal ulong ModId;
        internal MapLocationEntry Geometry;
        internal List<List<double>> Condition;
        internal List<List<double>> NormalCondition, ClickCondition;
        internal List<List<float>> Effects;
    }

    internal sealed class SceneObjectRegistry
    {
        internal const string FileName = "SceneObjectscfg.json";
        internal readonly List<SceneObjectEntry> Entries = new List<SceneObjectEntry>();
        internal int FileCount;
        internal static SceneObjectRegistry Load(IReadOnlyList<ContentRoot> roots)
        {
            var registry = new SceneObjectRegistry();
            foreach (ContentRoot source in WorldMapsContent.Sources(roots))
            {
                string path = FeatureContentDiscovery.ChildFile(source.Path, FileName);
                if (!File.Exists(path)) continue;
                registry.FileCount++;
                JObject document = JObject.Parse(File.ReadAllText(path));
                int schema = document.Value<int?>("schema") ?? 0;
                if ((schema != 1 && schema != 2) || !(document["objects"] is JArray rows))
                    throw new InvalidDataException(path + ": 需要 schema=1/2 和 objects 数组。");
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (JObject row in rows)
                {
                    SceneObjectEntry entry = Parse(row, path);
                    if (schema == 2 && !entry.Modern) throw new InvalidDataException(path + ": schema=2 需要 name、bgId 和四种新点击行为。");
                    entry.ModId = WorldMapsContent.ModId(source);
                    if (!keys.Add(entry.Key)) throw new InvalidDataException(path + ": 对象 key 重复：" + entry.Key);
                    registry.Entries.Add(entry);
                }
            }
            return registry;
        }
        internal static SceneObjectEntry Parse(JObject row, string path)
        {
            JObject bg = row["background"] as JObject ?? new JObject();
            JObject click = row["click"] as JObject ?? new JObject();
            var entry = new SceneObjectEntry {
                Key = row.Value<string>("key"), MapId = row.Value<int?>("mapId") ?? 0,
                PersonId = row.Value<int?>("personId") ?? 0,
                Background = bg.Value<string>("source") ?? "bg", Stage = bg.Value<string>("stage") ?? "auto",
                Url = bg.Value<string>("url"), Visibility = row.Value<string>("visibility") ?? "eventOrIntroduction",
                Click = click.Value<string>("type") ?? "sceneItem", Target = click.Value<int?>("target") ?? 0,
                Condition = row["condition"]?.ToObject<List<List<double>>>(),
                Effects = click["effects"]?.ToObject<List<List<float>>>()
            };
            entry.Modern = row["bgId"] != null;
            entry.Name = row.Value<string>("name") ?? entry.Key;
            entry.BackgroundId = row.Value<int?>("bgId") ?? 0;
            entry.NormalCondition = ReadConditions(row["normalCondition"], path);
            entry.ClickCondition = ReadConditions(row["clickCondition"], path);
            if (string.IsNullOrWhiteSpace(entry.Key) || entry.MapId < 1 || entry.PersonId <= 0)
                throw new InvalidDataException(path + ": key、mapId、personId 无效。");
            if (entry.Modern)
            {
                if (string.IsNullOrWhiteSpace(entry.Name) || entry.BackgroundId <= 0 ||
                    !new[] { "introduction", "introductionEffects", "background", "scene" }.Contains(entry.Click))
                    throw new InvalidDataException(path + ": 对象名称、场景背景 ID 或点击行为无效。");
                if ((entry.Click == "background" || entry.Click == "scene") && entry.Target <= 0)
                    throw new InvalidDataException(path + ": 请填写有效的目标 BgId / MapId。");
                if (entry.Click == "introductionEffects" && (entry.Effects == null || entry.Effects.Count == 0))
                    throw new InvalidDataException(path + ": 请填写效果。");
                ReadConditions(click["effects"], path);
            }
            else if (!new[] { "bg", "bg2", "any", "url" }.Contains(entry.Background) ||
                !new[] { "auto", "primary", "middle" }.Contains(entry.Stage) ||
                !new[] { "eventOrIntroduction", "always" }.Contains(entry.Visibility) ||
                !new[] { "sceneItem", "changeScene", "changeBackground", "function", "effects" }.Contains(entry.Click))
                throw new InvalidDataException(path + ": 背景、显示或点击类型无效。");
            if (entry.Background == "url" && string.IsNullOrWhiteSpace(entry.Url))
                throw new InvalidDataException(path + ": url 背景需要原版逻辑资源 URL。");
            if ((entry.Click == "changeScene" && entry.Target <= 1) || (entry.Click == "function" && entry.Target <= 0) ||
                (entry.Click == "effects" && (entry.Effects == null || entry.Effects.Count == 0)))
                throw new InvalidDataException(path + ": 点击目标或效果无效；回家须沿用原版返回流程。");
            JObject geometry = (JObject)row.DeepClone();
            geometry["mapId"] = Math.Max(2, entry.MapId);
            entry.Geometry = MapLocationRegistry.Parse(geometry, path);
            return entry;
        }
        private static List<List<double>> ReadConditions(JToken token, string path)
        {
            if (token == null) return new List<List<double>>();
            if (!(token is JArray groups) || groups.Any(g => !(g is JArray a) || a.Count == 0 ||
                a.Any(n => (n.Type != JTokenType.Integer && n.Type != JTokenType.Float) || double.IsNaN(n.Value<double>()) || double.IsInfinity(n.Value<double>()))))
                throw new InvalidDataException(path + ": 条件/效果须为有限数字组成的二维数组；制作台会自动转换原版分号写法。");
            return groups.ToObject<List<List<double>>>();
        }
        internal static bool MatchesBackgroundId(SceneObjectEntry entry, int id) => entry.BackgroundId == id;
        internal static bool MatchesBackground(SceneObjectEntry entry, bool other, string url, bool middle)
        {
            if (entry.Stage == "primary" && middle || entry.Stage == "middle" && !middle) return false;
            if (entry.Background == "any") return true;
            if (entry.Background == "url")
                return string.Equals(entry.Url, url, StringComparison.Ordinal) ||
                    string.Equals(entry.Url.StartsWith("Mods", StringComparison.Ordinal) ? Singleton<ModCtrl>.Ins.GetFullUrl(entry.Url, null) : entry.Url, url, StringComparison.Ordinal);
            return entry.Background == (other ? "bg2" : "bg");
        }
    }

    internal static class SceneObjectsRuntime
    {
        internal static SceneObjectRegistry Registry;
        internal static int Revision;
        private static ReloadInputSnapshot _inputs;
        private static ReloadInputSnapshot CaptureInputs(IReadOnlyList<ContentRoot> roots) => ReloadInputSnapshot.Capture(
            WorldMapsContent.Sources(roots).Select(root => FeatureContentDiscovery.ChildFile(root.Path, SceneObjectRegistry.FileName)), true);
        internal static void SetEffect(int person, int mode)
        {
            if (Registry == null || Cfg.PersonCfgMap == null || !Cfg.PersonCfgMap.ContainsKey(person) ||
                !Registry.Entries.Any(e => e.PersonId == person && WorldMapsContent.Enabled(e.ModId) && SceneObjectEffectState.HasImage(e, mode)))
            {
                PatchLog.Warning("场景物件效果未执行：PersonCfg " + person + " 未绑定已启用的场景对象或所需图片。");
                return;
            }
            (Math.Abs(mode) == 1 ? WorldMapState.ObjectNormal : WorldMapState.ObjectClick)[person] = mode > 0;
            foreach (SceneObjectSession session in UnityEngine.Object.FindObjectsOfType<SceneObjectSession>()) session.Refresh();
        }
        internal static void Install(Harmony harmony, IReadOnlyList<ContentRoot> roots)
        {
            Registry = SceneObjectRegistry.Load(roots);
            _inputs = CaptureInputs(roots);
            SceneObjectIntroduction.Install(harmony);
            foreach (Type type in new[] { typeof(MapSceneView), typeof(MainView) })
            {
                foreach (string name in new[] { "OnOpen", "Refresh", type == typeof(MapSceneView) ? "RefreshBtns" : "RefreshSceneItem" })
                    harmony.Patch(AccessTools.Method(type, name, Type.EmptyTypes),
                        postfix: new HarmonyMethod(typeof(SceneObjectsRuntime), nameof(Refresh)));
                harmony.Patch(AccessTools.Method(type, "OnClose", Type.EmptyTypes),
                    postfix: new HarmonyMethod(typeof(SceneObjectsRuntime), nameof(Close)));
            }
            JsonHotReloadApi.EnsureBuiltIn("up.map-extend.scene-objects", "地图扩展与优化/场景对象", Reload, 261);
            foreach (string method in new[] { "ChangeScene", "ChangeOtherBg" })
                harmony.Patch(AccessTools.Method(typeof(MapSceneView), method),
                    prefix: new HarmonyMethod(typeof(SceneObjectsRuntime), nameof(ResetBackground)));
            PatchLog.Registration("机制模块-场景对象登记：" + Registry.Entries.Count);
        }
        private static JsonHotReloadResult Reload()
        {
            try
            {
                var roots = ContentRootCatalog.Discover().Roots;
                var inputs = CaptureInputs(roots);
                if (inputs.SameAs(_inputs)) return JsonHotReloadResult.Skip("场景对象配置及引用图片无变化");
                SceneObjectRegistry replacement = SceneObjectRegistry.Load(roots);
                Registry = replacement; Revision++;
                foreach (SceneObjectSession session in UnityEngine.Object.FindObjectsOfType<SceneObjectSession>()) session.Refresh();
                _inputs = inputs;
                return JsonHotReloadResult.Completed(replacement.FileCount, replacement.Entries.Count, "当前场景对象已刷新；原版 CFG 新增后须重启游戏。");
            }
            catch (Exception ex) { return JsonHotReloadResult.Failed(ModuleHost.GetReason(ex)); }
        }
        public static void Refresh(BaseView __instance)
        {
            if (__instance?.gameObject == null) return;
            SceneObjectSession session = __instance.gameObject.GetComponent<SceneObjectSession>();
            if (session == null) session = __instance.gameObject.AddComponent<SceneObjectSession>();
            session.View = __instance;
            session.Refresh();
        }
        public static void Close(BaseView __instance) => __instance?.gameObject?.GetComponent<SceneObjectSession>()?.Release();
        public static void ResetBackground(MapSceneView __instance) => __instance.gameObject.GetComponent<SceneObjectSession>()?.ResetBackground();
        internal static int WorldForMap(int map)
        {
            int parent = MapFloorCompatibilityService.RootMapId(map);
            WorldMapDefinition world = WorldMapsRuntime.Worlds.FirstOrDefault(w => w.Id != 0 && w.MapIds.Contains(parent));
            return world?.Id ?? 0;
        }
    }

    internal sealed class SceneObjectSession : MonoBehaviour
    {
        internal BaseView View;
        private readonly List<SceneObjectNode> _nodes = new List<SceneObjectNode>();
        private TextureCache _textures;
        private int _map = -1, _revision = -1;
        private bool _refreshing;
        private int _backgroundOverride = -1, _backgroundMap = -1;
        private UISprite _homeBackground;
        private static readonly System.Reflection.FieldInfo BgId = AccessTools.Field(typeof(MapSceneView), "bgId");
        private static readonly System.Reflection.FieldInfo BgUrl = AccessTools.Field(typeof(MapSceneView), "bgUrl");
        private static readonly System.Reflection.FieldInfo Other = AccessTools.Field(typeof(MapSceneView), "isOtherBg");
        private static readonly System.Reflection.MethodInfo Change = AccessTools.Method(typeof(MapSceneView), "ChangeScene");
        private static readonly System.Reflection.MethodInfo ChangeBg = AccessTools.Method(typeof(MapSceneView), "ChangeOtherBg");
        private int MapId => View is MapSceneView scene ? scene.mapId : 1;
        internal int CurrentBg => _backgroundOverride > 0 && _backgroundMap == MapId ? _backgroundOverride : View is MapSceneView scene ? (int)BgId.GetValue(scene) : Cfg.MapCfgMap[1].bg;
        internal SceneIntroductionVisual Introduction;
        internal Image BackgroundImage => View is MapSceneView scene ? scene.icon_bg.image :
            _homeBackground?.image ?? ((MainView)View).btn_bg.gameObject.GetComponent<Image>();
        internal void EndHover() { foreach (SceneObjectNode node in _nodes) node.EndHover(); }
        internal IEnumerable<Image> IntroductionImages()
        {
            foreach (SceneObjectNode node in _nodes)
            {
                node.EndHover();
                if (node.gameObject.activeSelf && node.NormalAllowed && node.Normal != null) yield return node.Normal;
            }
        }
        internal void Refresh()
        {
            if (_refreshing || View == null || SceneObjectsRuntime.Registry == null) return;
            _refreshing = true;
            try
            {
                int map = MapId;
                if (!Cfg.MapCfgMap.ContainsKey(map)) return;
                if (_backgroundMap != map) ResetBackground();
                if (_backgroundOverride > 0) ApplyBackground();
                if (_map != map || _revision != SceneObjectsRuntime.Revision)
                {
                    ReleaseNodes(); _textures = new TextureCache();
                    Transform parent = View is MapSceneView scene ? scene.icon_bg.transform : ((MainView)View).btn_bg.transform;

                    var claimed = new HashSet<string>(StringComparer.Ordinal);
                    foreach (SceneObjectEntry entry in SceneObjectsRuntime.Registry.Entries.Where(e => e.MapId == map && WorldMapsContent.Enabled(e.ModId)).OrderBy(e => e.Geometry.Priority))
                    {
                        if (!claimed.Add(entry.ModId + ":" + entry.Key)) { PatchLog.Warning("场景对象 key 冲突，保留先登记项：" + entry.Key); continue; }
                        if (!Cfg.PersonCfgMap.ContainsKey(entry.PersonId)) { PatchLog.Warning("场景对象缺少已注册 PersonCfg：" + entry.PersonId); continue; }
                        int count = _nodes.Count;
                        try { Create(parent, entry); }
                        catch (Exception ex)
                        {
                            while (_nodes.Count > count) { SceneObjectNode failed = _nodes[_nodes.Count-1]; failed.gameObject.SetActive(false); UnityEngine.Object.Destroy(failed.gameObject); _nodes.RemoveAt(_nodes.Count-1); }
                            PatchLog.Warning("场景对象创建失败：" + entry.Key + ": " + ModuleHost.GetReason(ex));
                        }
                    }
                    _map = map; _revision = SceneObjectsRuntime.Revision;
                }
                bool other = View is MapSceneView s && (bool)Other.GetValue(s);
                string url = View is MapSceneView sv ? (string)BgUrl.GetValue(sv) : Cfg.BgCfgMap[CurrentBg].GetBgUrl(-1);
                bool middle = Singleton<RoleMgr>.Ins.GetRole()?.GradeState > 0;
                foreach (SceneObjectNode node in _nodes)
                {
                    SceneObjectEntry e = node.Entry;
                    bool visible = e.Modern ? SceneObjectRegistry.MatchesBackgroundId(e, CurrentBg) :
                        SceneObjectRegistry.MatchesBackground(e, other, url, middle) && CommonEvtMgr.IsMatchCondition(e.Condition, true);
                    if (e.Click == "sceneItem" && e.Visibility == "eventOrIntroduction")
                        visible &= Singleton<CommonEvtMgr>.Ins.HasSceneItemEvt(e.PersonId) || !string.IsNullOrEmpty(Cfg.PersonCfgMap[e.PersonId].introduction);
                    if (e.Click == "changeScene") visible &= CanNavigate(e.Target);
                    if (e.Modern)
                    {
                        node.gameObject.SetActive(visible);
                        node.SetConditions(SceneObjectEffectState.Resolve(e, CommonEvtMgr.IsMatchCondition(e.NormalCondition, true), false),
                            SceneObjectEffectState.Resolve(e, CommonEvtMgr.IsMatchCondition(e.ClickCondition, true), true));
                    }
                    else
                    {
                        bool normal = SceneObjectEffectState.Resolve(e, visible, false), click = SceneObjectEffectState.Resolve(e, visible, true);
                        node.gameObject.SetActive(SceneObjectRegistry.MatchesBackground(e, other, url, middle) && (normal || click));
                        node.SetConditions(normal, click);
                    }
                }
            }
            catch (Exception ex) { foreach (SceneObjectNode node in _nodes) node.gameObject.SetActive(false); PatchLog.Warning("场景对象刷新失败：" + ModuleHost.GetReason(ex)); }
            finally { _refreshing = false; }
        }
        private void Create(Transform parent, SceneObjectEntry entry)
        {
            var go = new GameObject("UP.SceneObject." + entry.Key, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = entry.Geometry.Position; rect.sizeDelta = entry.Geometry.Size;
            SceneObjectNode node = go.AddComponent<SceneObjectNode>(); node.Entry = entry; _nodes.Add(node);
            SceneObjectHitImage hit = go.AddComponent<SceneObjectHitImage>(); hit.Node = node;
            hit.color = new Color(1, 1, 1, .001f); hit.canvasRenderer.cullTransparentMesh = false;
            // UIButton otherwise adds an EmptyGraphic, bypassing polygon/alpha hit testing.
            Button button = go.AddComponent<Button>(); button.targetGraphic = hit; node.Button = button; node.Hit = hit;
            button.transition = Selectable.Transition.ColorTint;
            node.Normal = Visual(rect, entry.Geometry.ImagePath, entry.Geometry.ImagePosition - entry.Geometry.Position, entry.Geometry.ImageSize);
            node.Hover = Visual(rect, entry.Geometry.HoverImagePath, entry.Geometry.HoverPosition - entry.Geometry.Position, entry.Geometry.HoverSize);
            if (entry.Geometry.Polygon == null && node.Normal == null) throw new InvalidDataException("透明命中需要有效 PNG");
            node.Hover?.gameObject.SetActive(false);
            new UIButton(go).AddClick(() => Click(entry), Cfg.PersonCfgMap[entry.PersonId].clickAudio, 0);
        }
        private Image Visual(RectTransform parent, string path, Vector2 pos, Vector2 size)
        {
            if (path == null) return null;
            if (!_textures.TryGetSprite(path, out Sprite sprite)) throw new IOException("图片无法读取：" + path);
            var go = new GameObject("visual", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform; rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f); rect.anchoredPosition = pos; rect.sizeDelta = size;
            Image image = go.GetComponent<Image>(); image.sprite = sprite; image.raycastTarget = false; return image;
        }
        private bool CanNavigate(int target)
        {
            return target > 1 && Cfg.MapCfgMap.TryGetValue(target, out MapCfg cfg) && cfg.bg > 0 && Cfg.BgCfgMap.ContainsKey(cfg.bg) &&
                Singleton<FuncMgr>.Ins.GetMapData().IsUnlockMapScene(target, false) && CommonEvtMgr.IsMatchCondition(cfg.cond, true) &&
                SceneObjectsRuntime.WorldForMap(target) == (View is MainView ? 0 : WorldMapsRuntime.ActiveWorldId);
        }
        private void Click(SceneObjectEntry entry)
        {
            if (!UIMgr.IsTopView(View) || UIMgr.IsShowingMask() || View is MapSceneView greeting && greeting.HasGreeting()) return;
            if (entry.Modern ? !SceneObjectRegistry.MatchesBackgroundId(entry, CurrentBg) || !SceneObjectEffectState.Resolve(entry, CommonEvtMgr.IsMatchCondition(entry.ClickCondition, true), true) : !SceneObjectEffectState.Resolve(entry, CommonEvtMgr.IsMatchCondition(entry.Condition, true), true)) return;
            try
            {
                if (entry.Modern)
                {
                    if (entry.Click == "scene") MapMoveService.MoveTo(entry.Target);
                    else if (entry.Click == "background") SwitchBackground(entry.Target);
                    else
                    {
                        string intro = Cfg.PersonCfgMap[entry.PersonId].introduction;
                        if (!string.IsNullOrEmpty(intro)) SceneObjectIntroduction.ShowTalk(-1, intro, null, 0, -1, CurrentBg, View);
                        if (entry.Click == "introductionEffects") CommonEvtMgr.RunEffector(entry.Effects, null, 0, 0, null, 1f, false);
                    }
                }
                else if (entry.Click == "changeScene")
                {
                    if (!CanNavigate(entry.Target)) return;
                    if (View is MapSceneView scene) Change.Invoke(scene, new object[] { entry.Target });
                    else { View.CloseView(); UIMgr.OpenView<MapSceneView>(UILayerType.None, null, new object[] { entry.Target }); }
                }
                else if (entry.Click == "changeBackground") { if (View is MapSceneView scene && Cfg.MapCfgMap[MapId].bg2 > 0) ChangeBg.Invoke(scene, null); }
                else if (entry.Click == "function") Singleton<FuncMgr>.Ins.OpenFuncView(entry.Target);
                else if (entry.Click == "effects") CommonEvtMgr.RunEffector(entry.Effects, null, 0, 0, null, 1f, false);
                else if (!Singleton<CommonEvtMgr>.Ins.ShowSceneItemEvt(entry.PersonId, CurrentBg))
                {
                    string text = Cfg.PersonCfgMap[entry.PersonId].introduction;
                    if (!string.IsNullOrEmpty(text)) SceneObjectIntroduction.ShowTalk(-1, text, null, 0, -1, CurrentBg, View);
                }
                Refresh();
            }
            catch (Exception ex) { PatchLog.Warning("场景对象点击失败：" + entry.Key + ": " + ModuleHost.GetReason(ex)); }
        }
        private void SwitchBackground(int target)
        {
            if (!Cfg.BgCfgMap.ContainsKey(target)) throw new InvalidDataException("BgCfg 未注册背景：" + target);
            int map = MapId;
            UIMgr.ShowCycleMask(Vector2.zero, () => {
                if (View == null || MapId != map) return;
                _backgroundOverride = target; _backgroundMap = map;
                Refresh();

            }, .4f, true, false);
        }
        private void ApplyBackground()
        {
            string url = Cfg.BgCfgMap[_backgroundOverride].GetBgUrl(-1);
            if (View is MapSceneView scene)
            {
                BgId.SetValue(scene, _backgroundOverride); BgUrl.SetValue(scene, url);
                Other.SetValue(scene, Cfg.MapCfgMap[MapId].bg2 == _backgroundOverride);
                scene.icon_bg.SetTextureUrl(url, true);
                Singleton<FuncMgr>.Ins.RecordMap(MapId, _backgroundOverride, -1);
                AccessTools.Method(typeof(MapSceneView), "RefreshAction").Invoke(scene, null);
                AccessTools.Method(typeof(MapSceneView), "RefreshNPC").Invoke(scene, null);
                AccessTools.Method(typeof(MapSceneView), "RefreshBtns").Invoke(scene, null);
            }
            else
            {
                if (_homeBackground == null)
                {
                    var go = new GameObject("UP.SceneBackground", typeof(RectTransform), typeof(Image));
                    var rect = (RectTransform)go.transform;
                    rect.SetParent(((MainView)View).btn_bg.transform, false); rect.SetAsFirstSibling();
                    rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
                    _homeBackground = new UISprite(go); _homeBackground.image.raycastTarget = false;
                }
                _homeBackground.SetTextureUrl(url, true);
            }
            Singleton<RoleMgr>.Ins.GetPhoneData(true).AddPhoto(url);
        }
        internal void ResetBackground()
        {
            _backgroundOverride = _backgroundMap = -1;
            if (_homeBackground != null) { UnityEngine.Object.Destroy(_homeBackground.gameObject); _homeBackground = null; }
        }
        private void ReleaseNodes()
        {
            foreach (SceneObjectNode node in _nodes) if (node != null) { node.gameObject.SetActive(false); UnityEngine.Object.Destroy(node.gameObject); }
            _nodes.Clear(); _textures?.Dispose(); _textures = null; _map = _revision = -1;
        }
        internal void Release() { Introduction?.Release(); ReleaseNodes(); ResetBackground(); }
        private void OnDestroy() => Release();
    }
    internal sealed class SceneObjectNode : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
    {
        internal SceneObjectEntry Entry;
        internal Image Normal, Hover;
        internal Button Button;
        internal SceneObjectHitImage Hit;
        private bool _normalAllowed = true, _clickAllowed = true, _hovered;
        internal bool NormalAllowed => _normalAllowed;
        internal void EndHover() { _hovered = false; UpdateVisual(); }
        internal void SetConditions(bool normal, bool click)
        {
            _normalAllowed = normal; _clickAllowed = click;
            Button.interactable = click; Hit.raycastTarget = click;
            if (!click) _hovered = false;
            UpdateVisual();
        }
        private void UpdateVisual()
        {
            bool hover = _hovered && _clickAllowed && Hover != null;
            Hover?.gameObject.SetActive(hover);
            Normal?.gameObject.SetActive(_normalAllowed && !hover);
        }
        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData data) { _hovered = true; UpdateVisual(); }
        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData data) { _hovered = false; UpdateVisual(); }
        private void OnDisable() { _hovered = false; UpdateVisual(); }
    }
    internal sealed class SceneObjectHitImage : Image
    {
        internal SceneObjectNode Node;
        public override bool IsRaycastLocationValid(Vector2 screen, Camera camera)
        {
            if (Node == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screen, camera, out Vector2 point)) return false;
            MapLocationEntry e = Node.Entry.Geometry;
            if (e.Polygon != null) return MapLocationGeometry.Contains(e.Polygon, point);
            Texture2D texture = Node.Normal?.sprite?.texture;
            if (texture == null) return false;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Node.Normal.rectTransform, screen, camera, out point)) return false;
            Vector2 size = Node.Normal.rectTransform.rect.size;
            float u = point.x / size.x + .5f, v = point.y / size.y + .5f;
            return u >= 0 && u <= 1 && v >= 0 && v <= 1 && texture.GetPixel(Mathf.Min(texture.width-1, (int)(u*texture.width)), Mathf.Min(texture.height-1, (int)(v*texture.height))).a >= e.AlphaThreshold;
        }
    }
}
