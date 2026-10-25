using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using Config;
using EC2BUnofficialPatch.Core;
using EC2BUnofficialPatch.Resources;
using EC2BUnofficialPatch.Workshop;
using GenUI.Main;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Sdk;
using UnityEngine;
using UnityEngine.UI;
using View.Main;

namespace EC2BUnofficialPatch.Features.Mechanics.MapExtend
{
    internal static class WorldMapsContent
    {
        internal const string Folder = "MapExtend", FileName = "Worldmapscfg.json";
        internal static readonly string LocalRoot = Path.Combine(Paths.PluginPath, "EC2BUnofficialPatch", Folder);
        internal static IEnumerable<ContentRoot> Sources(IReadOnlyList<ContentRoot> roots)
        {
            string oldLocal = Path.Combine(Paths.PluginPath, "EC2BUnofficialPatch", "WorldMaps");
            yield return new ContentRoot("local-worlds", new[] { FileName, "MapLocationscfg.json", "SceneObjectscfg.json" }.Any(n => File.Exists(Path.Combine(LocalRoot, n))) ? LocalRoot : oldLocal);
            foreach (ContentRoot root in roots ?? Array.Empty<ContentRoot>())
            {
                string[] dirs = FeatureContentDiscovery.Directories(root.Path, Folder).ToArray();
                if (dirs.Length == 0) dirs = FeatureContentDiscovery.Directories(root.Path, "WorldMaps").ToArray();
                foreach (string directory in dirs) yield return new ContentRoot(root.Id, directory);
            }
        }

        internal static ulong ModId(ContentRoot source)
        {
            if (source.Id == "local-worlds") return 0;
            string idText = source.Id.StartsWith("workshop-", StringComparison.OrdinalIgnoreCase)
                ? source.Id.Substring("workshop-".Length) : string.Empty;
            ulong.TryParse(idText, out ulong id);
            if (id == 0) throw new InvalidDataException("Workshop 内容来源需要数字 Mod ID：" + source.Id);
            return id;
        }
        internal static bool Enabled(ulong modId) => modId == 0 ||
            (Singleton<ModCtrl>.Ins.activeMods != null && Singleton<ModCtrl>.Ins.activeMods.Contains(modId));
        internal static string ImagePath(string config, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
                throw new InvalidDataException("大地图 url 需要目录内的 PNG 相对路径。");
            string directory = Path.GetFullPath(Path.GetDirectoryName(config)) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(directory, relative));
            if (!path.StartsWith(directory, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("大地图 PNG 路径越界或扩展名错误：" + relative);
            return path;
        }
    }
    internal sealed class WorldMapDefinition
    {
        internal int Id;
        internal string Name, ImagePath;
        internal Vector2 Size;
        internal bool Extension, DefaultUnlocked;
        internal int[] MapIds, AutoUnlockMaps, TravelMaps;
    }
    internal sealed class WorldMapsPackage
    {
        internal ulong ModId;
        internal readonly List<WorldMapDefinition> Worlds = new List<WorldMapDefinition>();
    }
    internal static class WorldMapsRuntime
    {
        internal const int FunctionId = 160;
        internal const int NativeStationId = 12;
        private const int ActionBaseId = 990000, SpareActionStart = 15000000;
        internal static int ActiveWorldId { get; private set; }
        private static int _pendingWorld = -1;
        private static bool _preparing, _returnRequired;
        private static List<WorldMapsPackage> _packages;
        internal static bool IsInstalled => _packages != null;
        internal static readonly List<WorldMapDefinition> Worlds = new List<WorldMapDefinition>();
        private static readonly Dictionary<int, ActionCfg> OwnedActions = new Dictionary<int, ActionCfg>();
        private static readonly Dictionary<int, int> TravelActions = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> RemappedActions = new Dictionary<int, int>();
        private static readonly HashSet<int> MissingWorlds = new HashSet<int>();
        private static readonly FuncCfg TravelFunction = new FuncCfg { id = FunctionId, name = "去其他地方",
            type = 0, icon = "common2/icon_trip", parm = new List<int>(), tips = null, tipsType = 0,
            chain = new List<int>(), preFunc = 0, redpoint = 0, debug = 0 };
        internal static Sprite NativeOverviewSprite;

        internal static void Install(Harmony harmony)
        {
            ReloadContent(ContentRootCatalog.Discover().Roots);
            Patch(harmony, typeof(FuncMgr), "Init", nameof(NewGame), null);
            // Register before the native foreach, never from inside OpenFunc.
            InstallFunctionHooks(harmony);
            Patch(harmony, typeof(FuncMgr), "Load", nameof(LoadPrefix), null);
            Patch(harmony, typeof(FuncMgr), "LoadEnd", nameof(StartPrefix), null);
            Patch(harmony, typeof(FuncMgr), "Save", null, nameof(SavePostfix));
            harmony.Patch(AccessTools.Method(typeof(FuncMgr), "Start", new[] { typeof(Action) }),
                prefix: new HarmonyMethod(typeof(WorldMapsRuntime), nameof(StartPrefix)));
            harmony.Patch(AccessTools.Method(typeof(FuncMgr), "OpenFuncView", new[] { typeof(int) }),
                prefix: new HarmonyMethod(typeof(WorldMapsRuntime), nameof(OpenFunction)));
            harmony.Patch(AccessTools.Method(typeof(ActionData), "GetActions", new[] { typeof(int), typeof(int) }),
                prefix: new HarmonyMethod(typeof(WorldMapsRuntime), nameof(BeforeActions)),
                postfix: new HarmonyMethod(typeof(WorldMapsRuntime), nameof(FilterActions)));
            Patch(harmony, typeof(MapSceneView), "OnOpen", nameof(SceneOpening), null);
            Patch(harmony, typeof(MapSceneView), "OnBeforeCloseAnime", nameof(SceneClosing), null);
            MapExtendEffects.Install(harmony);
        }
        private static void Patch(Harmony harmony, Type type, string method, string prefix, string postfix) =>
            harmony.Patch(AccessTools.Method(type, method, Type.EmptyTypes) ?? throw new MissingMethodException(type.FullName, method),
                prefix: prefix == null ? null : new HarmonyMethod(typeof(WorldMapsRuntime), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(WorldMapsRuntime), postfix));

        internal static void InstallFunctionHooks(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(FuncMgr), "SetIsGuideMode", new[] { typeof(GameMode) }),
                prefix: new HarmonyMethod(typeof(WorldMapsRuntime), nameof(EnsureFunction)));
            harmony.Patch(AccessTools.Method(typeof(FuncData), "OpenFunc", new[] { typeof(int), typeof(bool) }),
                postfix: new HarmonyMethod(typeof(WorldMapsRuntime), nameof(FunctionOpened)));
        }

        internal static void ReloadContent(IReadOnlyList<ContentRoot> roots)
        {
            var replacement = new List<WorldMapsPackage>();
            foreach (ContentRoot source in WorldMapsContent.Sources(roots))
            {
                string path = FeatureContentDiscovery.ChildFile(source.Path, WorldMapsContent.FileName);
                if (!File.Exists(path)) continue;
                JObject document = JObject.Parse(File.ReadAllText(path));
                if (document.Value<int?>("schema") != 1 || !(document["worlds"] is JArray worlds))
                    throw new InvalidDataException(path + ": 需要 schema=1 和 worlds 数组。");
                var package = new WorldMapsPackage { ModId = WorldMapsContent.ModId(source) };
                var ids = new HashSet<int>();
                foreach (JToken token in worlds)
                {
                    JObject row = token as JObject ?? throw new InvalidDataException(path + ": 区域必须为对象。");
                    int id = row.Value<int>("id");
                    bool extension = id != 0 && (row.Value<bool?>("extension") ?? false);
                    JArray size = row["coordinateSize"] as JArray;
                    if (size != null && (size.Count != 2 || size.Any(value => value.Type != JTokenType.Integer && value.Type != JTokenType.Float)))
                        throw new InvalidDataException(path + ": coordinateSize 需要两个数字。");
                    var world = new WorldMapDefinition { Id = id, Name = id == 0 ? "鹅城" : row.Value<string>("name"),
                        Extension = extension, DefaultUnlocked = id == 0 || (row.Value<bool?>("defaultUnlocked") ?? false),
                        ImagePath = id == 0 || extension ? null : WorldMapsContent.ImagePath(path, row.Value<string>("url")),
                        Size = size == null ? new Vector2(2560, 1440) : new Vector2((float)size[0], (float)size[1]),
                        MapIds = Ids(row, "mapIds"), AutoUnlockMaps = Ids(row, "autoUnlockMaps"), TravelMaps = Ids(row, "travelMaps") };
                    if (id < 0 || id > 16777215 || !ids.Add(id) || (!extension && string.IsNullOrWhiteSpace(world.Name)) ||
                        float.IsNaN(world.Size.x) || float.IsInfinity(world.Size.x) || world.Size.x <= 0 ||
                        float.IsNaN(world.Size.y) || float.IsInfinity(world.Size.y) || world.Size.y <= 0 ||
                        (id != 0 && !extension && (world.MapIds.Length == 0 || world.TravelMaps.Length == 0)) ||
                        world.TravelMaps.Any(map => !world.MapIds.Contains(map)))
                        throw new InvalidDataException(path + ": 区域 ID/尺寸/父地点列表无效；地图 ID 为0～16777215，新区域必须有车站。");
                    package.Worlds.Add(world);
                }
                replacement.Add(package);
            }
            MissingWorlds.Clear(); Compose(replacement);
            _packages = replacement;
        }
        private static int[] Ids(JObject row, string field)
        {
            JToken token = row[field];
            if (token == null) return Array.Empty<int>();
            if (!(token is JArray array) || array.Any(value => value.Type != JTokenType.Integer || value.Value<int>() <= 1))
                throw new InvalidDataException(field + " 必须为大于 1 的地点 ID 数组。");
            return array.Values<int>().Distinct().ToArray();
        }
        private static void Compose(List<WorldMapsPackage> packages)
        {
            var rows = packages.Where(package => WorldMapsContent.Enabled(package.ModId)).SelectMany(package => package.Worlds).ToList();
            var worlds = new List<WorldMapDefinition> { new WorldMapDefinition { Id = 0, Name = "鹅城", DefaultUnlocked = true,
                Size = new Vector2(2560, 1440), MapIds = new[] { NativeStationId }, AutoUnlockMaps = Array.Empty<int>(),
                TravelMaps = new[] { NativeStationId } } };
            foreach (WorldMapDefinition row in rows.Where(row => row.Id != 0 && !row.Extension))
            {
                if (worlds.Any(item => item.Id == row.Id)) throw new InvalidDataException("同一大地图只能有一份底图定义；其他 Mod 请使用 extension=true：" + row.Id);
                worlds.Add(new WorldMapDefinition { Id = row.Id, Name = row.Name, ImagePath = row.ImagePath, Size = row.Size,
                    DefaultUnlocked = row.DefaultUnlocked, MapIds = Array.Empty<int>(), AutoUnlockMaps = Array.Empty<int>(), TravelMaps = Array.Empty<int>() });
            }
            foreach (WorldMapDefinition row in rows)
            {
                WorldMapDefinition target = worlds.FirstOrDefault(world => world.Id == row.Id);
                if (target == null) { if (MissingWorlds.Add(row.Id)) PatchLog.Warning("大地图贡献暂未启用：缺少底图定义 worldId=" + row.Id); continue; }
                target.MapIds = target.MapIds.Concat(row.MapIds).Distinct().ToArray();
                target.AutoUnlockMaps = target.AutoUnlockMaps.Concat(row.AutoUnlockMaps).Distinct().ToArray();
                target.TravelMaps = target.TravelMaps.Concat(row.TravelMaps).Distinct().ToArray();
            }
            var owners = new Dictionary<int, int>();
            foreach (WorldMapDefinition world in worlds)
                foreach (int map in world.MapIds)
                {
                    if (owners.TryGetValue(map, out int owner) && owner != world.Id)
                        throw new InvalidDataException("同一父地点不能属于多张大地图：mapId=" + map);
                    owners[map] = world.Id;
                }
            Worlds.Clear(); Worlds.AddRange(worlds);
        }
        public static void EnsureFunction()
        {
            // Reserved builtin row wins after the native Mod loader has finished.
            if (Cfg.FuncCfgMap != null && (!Cfg.FuncCfgMap.TryGetValue(FunctionId, out FuncCfg current) ||
                !ReferenceEquals(current, TravelFunction))) Cfg.FuncCfgMap[FunctionId] = TravelFunction;
        }
        internal static void EnsureCfg()
        {
            if (_packages == null) return;
            Compose(_packages); EnsureFunction();
            if (Cfg.MapCfgMap == null || Cfg.ActionCfgMap == null || Cfg.BgCfgMap == null) return;
            TravelActions.Clear();
            var reserved = new HashSet<int>(WorldMapState.ActionIds.Values);
            foreach (int map in Worlds.SelectMany(world => world.TravelMaps).Distinct().OrderBy(id => id))
            {
                if (!Cfg.MapCfgMap.TryGetValue(map, out MapCfg location) || location.type != 0)
                    throw new InvalidDataException("travelMaps 需要 MapCfg 中已注册的父地点：" + map);
                int previous = WorldMapState.ActionIds.TryGetValue(map, out int saved) ? saved : 0;
                long preferred = (long)ActionBaseId + map;
                int id = previous > 0 ? previous : preferred < int.MaxValue ? (int)preferred : SpareActionStart;
                if (!AvailableAction(id, map) || (previous == 0 && reserved.Contains(id)))
                {
                    id = SpareActionStart;
                    while (!AvailableAction(id, map) || reserved.Contains(id)) id++;
                }
                if (previous > 0 && previous != id) RemappedActions[map] = previous;
                WorldMapState.ActionIds[map] = id; reserved.Add(id); TravelActions[map] = id;
                if (OwnedActions.TryGetValue(id, out ActionCfg own) && Cfg.ActionCfgMap.TryGetValue(id, out ActionCfg current) && ReferenceEquals(own, current)) continue;
                var cfg = new ActionCfg { id = id, name = "去其他地方", map = map, funcId = FunctionId,
                    type = 1, icon = "trip", cost = new List<List<float>>(), effect = new List<List<float>>(),
                    attrs = new List<string>(), unlock = new List<List<double>> { new List<double> { 101, 1, FunctionId } },
                    expReward = new List<List<float>>(), interactable = new List<List<double>>(),
                    beginTime = new List<float>(), endTime = new List<float>(), disableTxt = new List<string>() };
                Cfg.ActionCfgMap[id] = cfg; OwnedActions[id] = cfg;
            }
        }
        private static bool AvailableAction(int id, int map) => !Cfg.ActionCfgMap.TryGetValue(id, out ActionCfg row) ||
            (OwnedActions.TryGetValue(id, out ActionCfg own) && ReferenceEquals(row, own) && own.map == map);
        private static void ClearOwnedActions()
        {
            if (Cfg.ActionCfgMap != null)
                foreach (var row in OwnedActions)
                    if (Cfg.ActionCfgMap.TryGetValue(row.Key, out ActionCfg current) && ReferenceEquals(current, row.Value)) Cfg.ActionCfgMap.Remove(row.Key);
            OwnedActions.Clear(); TravelActions.Clear(); RemappedActions.Clear();
        }
        public static void NewGame() { WorldMapState.Clear(); ClearOwnedActions(); ResetWorld(); EnsureCfg(); }
        public static void LoadPrefix()
        {
            ClearOwnedActions(); ResetWorld(); ActiveWorldId = WorldMapState.Load(); EnsureCfg();
            if (!IsWorldUnlocked(ActiveWorldId)) ActiveWorldId = 0;
        }
        internal static void ResetWorld() { ActiveWorldId = 0; _pendingWorld = -1; _returnRequired = false; WorldMapTransitions.Clear(); }
        public static void SavePostfix() => WorldMapState.Save(ActiveWorldId);
        public static void StartPrefix() => PreparePlayer();
        public static void SceneOpening(MapSceneView __instance)
        {
            PreparePlayer();
            if (__instance.gameObject.GetComponent<WorldMapReturnDriver>() == null) __instance.gameObject.AddComponent<WorldMapReturnDriver>();
        }
        internal static bool IsWorldUnlocked(int id)
        {
            WorldMapDefinition world = Worlds.FirstOrDefault(item => item.Id == id);
            return world != null && (id == 0 || (WorldMapState.Unlocked.TryGetValue(id, out bool opened) ? opened : world.DefaultUnlocked));
        }
        internal static void SetWorldUnlocked(int id, bool value)
        {
            if (id <= 0 || id > 16777215) return;
            WorldMapState.Unlocked[id] = value;
            if (!value && ActiveWorldId == id) _returnRequired = true;
            if (value) _returnRequired = !IsWorldUnlocked(ActiveWorldId);
            PreparePlayer();
            var selection = UIMgr.GetView(typeof(WorldTravelView).FullName) as WorldTravelView;
            if (selection != null && UIMgr.IsViewOpened<WorldTravelView>()) selection.RefreshDestinations();
        }
        // Travel parents must open with their world; their children remain independent.
        internal static IEnumerable<int> AutomaticUnlockMaps(WorldMapDefinition world) =>
            world.AutoUnlockMaps.Concat(world.TravelMaps).Where(id => !(world.Id == 0 && id == NativeStationId)).Distinct();
        private static void PreparePlayer()
        {
            if (_preparing || _packages == null) return;
            _preparing = true;
            try
            {
                EnsureCfg();
                foreach (WorldMapDefinition world in Worlds)
                {
                    if (!WorldMapState.Unlocked.ContainsKey(world.Id)) WorldMapState.Unlocked[world.Id] = world.DefaultUnlocked;
                    if (!IsWorldUnlocked(world.Id)) continue;
                    foreach (int id in AutomaticUnlockMaps(world))
                        if (Cfg.MapCfgMap.ContainsKey(id) && !WorldMapState.InitializedMaps.Contains(id))
                        {
                            Singleton<FuncMgr>.Ins.GetMapData().UnlockMapScene(id);
                            WorldMapState.InitializedMaps.Add(id);
                        }
                }
                if (Singleton<FuncMgr>.Ins.IsFuncOpened(FunctionId)) AddTravelActions();
            }
            finally { _preparing = false; }
        }
        private static void AddTravelActions()
        {
            foreach (var row in TravelActions)
                Singleton<RoleMgr>.Ins.GetActionData().AddAction(row.Value, -1, true);
        }
        public static void FunctionOpened(int _id)
        {
            if (_id == FunctionId && !_preparing && Singleton<FuncMgr>.Ins.IsFuncOpened(_id)) PreparePlayer();
        }
        public static void BeforeActions(ActionData __instance)
        {
            PreparePlayer();
            foreach (var row in RemappedActions.ToArray())
                if (__instance.actionDict.TryGetValue(row.Key, out List<ActionSubData> actions) && TravelActions.TryGetValue(row.Key, out int id))
                {
                    ActionSubData saved = actions.FirstOrDefault(action => action.id == row.Value);
                    if (saved != null)
                    {
                        actions.RemoveAll(action => action.id == id && !ReferenceEquals(action, saved));
                        saved.id = id;
                    }
                    RemappedActions.Remove(row.Key);
                }
        }
        public static void FilterActions(ref List<ActionSubData> __result)
        {
            if (__result == null) return;
            bool functionOpen = Singleton<FuncMgr>.Ins.IsFuncOpened(FunctionId);
            __result.RemoveAll(action => OwnedActions.ContainsKey(action.id) &&
                (!functionOpen || !TravelActions.Values.Contains(action.id) ||
                 !Worlds.Any(world => IsWorldUnlocked(world.Id) && world.TravelMaps.Contains(OwnedActions[action.id].map))));
        }
        public static bool OpenFunction(int _funcId)
        {
            EnsureCfg();
            if (_funcId != FunctionId) return true;
            PreparePlayer();
            Singleton<FuncMgr>.Ins.GetMapData().recordFuncId = 0;
            if (!Singleton<FuncMgr>.Ins.IsFuncOpened(FunctionId)) return false;
            if (UIMgr.GetView(typeof(WorldTravelView).FullName) == null)
            {
                object manager = AccessTools.Field(typeof(UIMgr), "ins").GetValue(null);
                var views = (Dictionary<string, BaseView>)AccessTools.Field(typeof(UIMgr), "viewDict").GetValue(manager);
                views.Add(typeof(WorldTravelView).FullName, new WorldTravelView());
            }
            UIMgr.OpenView<WorldTravelView>(); return false;
        }
        internal static MapLocationRegistry RegistryFor(MapLocationRegistry external)
        {
            PreparePlayer();
            if (_pendingWorld >= 0) { ActiveWorldId = IsWorldUnlocked(_pendingWorld) ? _pendingWorld : 0; _pendingWorld = -1; }
            if (!IsWorldUnlocked(ActiveWorldId)) ActiveWorldId = 0;
            WorldMapDefinition active = Worlds.First(world => world.Id == ActiveWorldId);
            var result = new MapLocationRegistry { FileCount = external.FileCount };
            result.Entries.AddRange(external.Entries.Where(entry => entry.WorldId == ActiveWorldId &&
                (ActiveWorldId == 0 || active.MapIds.Contains(entry.MapId))));
            if (ActiveWorldId != 0)
                foreach (int map in active.TravelMaps)
                    if (!result.Entries.Any(entry => entry.MapId == map && WorldMapsContent.Enabled(entry.ModId) && entry.ShowLabel) ||
                        !Cfg.MapCfgMap.TryGetValue(map, out MapCfg cfg) || (cfg.cond != null && cfg.cond.Count > 0))
                        throw new InvalidDataException("新大地图车站必须有可见标点且 MapCfg.cond 为空：" + map);
            return result;
        }
        internal static void SwitchTo(int id)
        {
            if (!IsWorldUnlocked(id)) return;
            Singleton<FuncMgr>.Ins.GetMapData().recordFuncId = 0;
            _pendingWorld = id; _returnRequired = false;
            if (UIMgr.IsViewOpened<MapSceneView>()) { UIMgr.CloseView<MapSceneView>(); return; }
            if (UIMgr.IsViewOpened<MapView>())
            {
                var view = (MapView)UIMgr.GetView<MapView>(false);
                WorldMapTransitions.Capture(view);
                view.OnClose(); view.OnOpen(); view.PlayViewAnime(true); return;
            }
            UIMgr.OpenView<MapView>();
        }
        public static void SceneClosing(MapSceneView __instance)
        {
            if (ActiveWorldId != 0 || _pendingWorld > 0) WorldMapTransitions.Capture(__instance);
        }
        internal static void ReturnIfRemoved()
        {
            if (!_returnRequired || UIMgr.IsShowingMask()) return;
            BaseView top = UIMgr.GetTopView(Array.Empty<ViewType>());
            if ((top is MapView || top is MapSceneView) && top.viewState == ViewState.Opened && top.enableCloseByRightClick) SwitchTo(0);
        }
        internal static void RequireStations(MapLocationsSession session)
        {
            if (ActiveWorldId == 0) return;
            WorldMapDefinition world = Worlds.First(item => item.Id == ActiveWorldId);
            foreach (int map in world.TravelMaps)
                if (!session.Markers.Any(marker => marker.Entry.MapId == map))
                    throw new InvalidDataException("车站标点未成功创建，已恢复鹅城；请检查该地点的背景/图片登记：" + map);
        }
    }

    // Reuse the native tourism window, scrolling cards, fonts and close button.
    internal sealed class WorldTravelView : TripUI
    {
        private int _selected = -1, _chosen = -1;
        private TextureCache _textures;
        private bool _nativeTitleHidden;
        public WorldTravelView()
        {
            isShowMask = true; enableCloseByRightClick = true; enableCloseByMask = false;
            layerType = UILayerType.Normal; openAnimeType = closeAnimeType = AnimeType.Fade; isShowLoading = true;
        }
        public override void InitUI()
        {
            base.InitUI();
            foreach (Transform child in group_star) child.gameObject.SetActive(false);
            Text title = UnityEngine.Object.Instantiate(Cell_TripItem.txt_name, group_star, false);
            title.name = "EC2B.TravelTitle"; title.text = "去其他地方"; title.alignment = TextAnchor.MiddleCenter;
            title.color = new Color(0.51f, 0.30f, 0.14f); title.fontSize = 36; title.raycastTarget = false;
            title.rectTransform.anchorMin = Vector2.zero; title.rectTransform.anchorMax = Vector2.one;
            title.rectTransform.offsetMin = title.rectTransform.offsetMax = Vector2.zero;
            title.rectTransform.localScale = Vector3.one; title.gameObject.SetActive(true);
            group_tag.gameObject.SetActive(false);
            InitItemGroup(itemgroup_data, Cell_TripItem, Render, Create, null, null, null, false);
            this.ShowCloseBtn(true, null);
        }
        public override void OnOpen()
        {
            base.OnOpen(); _selected = _chosen = -1;
            HideNativeTravelTitle();
            _textures = new TextureCache();
            RefreshDestinations();
            Scroll_View.verticalNormalizedPosition = 1;
        }
        internal void RefreshDestinations()
        {
            _selected = -1;
            var destinations = WorldMapsRuntime.Worlds.Where(world => world.Id != WorldMapsRuntime.ActiveWorldId &&
                WorldMapsRuntime.IsWorldUnlocked(world.Id)).ToList();
            itemgroup_data.SetDatas(destinations, null);
            Text title = group_star.Find("EC2B.TravelTitle")?.GetComponent<Text>();
            if (title != null) title.text = destinations.Count > 0 ? "去其他地方" : "去其他地方\n暂无已开放的目的地";
        }
        private void Create(UICell value)
        {
            var cell = (Cell_TripItemUI)value;
            cell.btn_click.AddClick(() => { _selected = ((WorldMapDefinition)cell.data).Id;
                foreach (UICell item in itemgroup_data.GetCells()) Render(item); });
            cell.btn_start.AddClick(() => {
                int id = ((WorldMapDefinition)cell.data).Id;
                if (!Singleton<FuncMgr>.Ins.IsFuncOpened(WorldMapsRuntime.FunctionId) || !WorldMapsRuntime.IsWorldUnlocked(id)) { RefreshDestinations(); return; }
                _chosen = id; CloseView(); });
        }
        private void HideNativeTravelTitle()
        {
            if (_nativeTitleHidden) return;
            Rect area = transform.rect;
            // The decorative travel logo is not bound by TripUI. Locate its small top-left graphic,
            // outside the star panel/list, without relying on an undocumented prefab node name.
            var candidates = gameObject.GetComponentsInChildren<Image>(true).Where(image =>
                image.sprite != null && image.enabled && image.gameObject.activeInHierarchy &&
                !image.transform.IsChildOf(group_star) && !image.transform.IsChildOf(Scroll_View.transform) &&
                image.GetComponent<Button>() == null).Select(image =>
                {
                    var corners = new Vector3[4];
                    image.rectTransform.GetWorldCorners(corners);
                    Vector2 min = transform.InverseTransformPoint(corners[0]), max = min;
                    foreach (Vector3 corner in corners)
                    {
                        Vector2 point = transform.InverseTransformPoint(corner);
                        min = Vector2.Min(min, point); max = Vector2.Max(max, point);
                    }
                    return new { Image = image, Center = (min + max) / 2, Size = max - min };
                }).Where(item => item.Size.x >= area.width * 0.04f && item.Size.x <= area.width * 0.3f &&
                    item.Size.y >= area.height * 0.03f && item.Size.y <= area.height * 0.25f &&
                    item.Size.x / item.Size.y >= 0.8f && item.Size.x / item.Size.y <= 6 &&
                    item.Center.x < area.xMin + area.width * 0.3f &&
                    item.Center.y > area.yMax - area.height * 0.2f).ToList();
            var logo = candidates.OrderByDescending(item =>
                (item.Image.name + "/" + item.Image.sprite.name).IndexOf("title", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (item.Image.name + "/" + item.Image.sprite.name).IndexOf("logo", StringComparison.OrdinalIgnoreCase) >= 0)
                .ThenByDescending(item => item.Size.x * item.Size.y).FirstOrDefault();
            if (logo == null)
            {
                PatchLog.Warning("目的地窗口未识别原版旅行标识；请提供界面截图与当前版本信息。");
                return;
            }
            logo.Image.enabled = false;
            _nativeTitleHidden = true;
        }
        private void Render(UICell value)
        {
            var cell = (Cell_TripItemUI)value;
            var world = (WorldMapDefinition)cell.data;
            cell.txt_name.text = world.Name; cell.txt_name.color = new Color(0.51f, 0.30f, 0.14f);
            cell.canvasgroup_click.alpha = 1;
            cell.group_detail.gameObject.SetActive(false); cell.img_star.gameObject.SetActive(false);
            cell.img_type.gameObject.SetActive(false); cell.finish.gameObject.SetActive(false);
            cell.highlight.gameObject.SetActive(world.Id == _selected);
            cell.group_btns.gameObject.SetActive(world.Id == _selected);
            cell.btn_prepare.gameObject.SetActive(false); cell.btn_start.gameObject.SetActive(true);
            cell.btn_click.interactable = cell.btn_start.interactable = true;
            cell.icon_bg.SetAtlasUrl("trip/img_item", true); cell.icon_start.SetAtlasUrl("trip/img_bus", true);
            Sprite sprite = WorldMapsRuntime.NativeOverviewSprite;
            if (world.Id != 0 && !_textures.TryGetSprite(world.ImagePath, out sprite))
                throw new InvalidDataException("目的地底图读取失败：" + world.ImagePath);
            cell.icon_item.image.sprite = sprite; cell.icon_item.image.preserveAspect = true;
            var effect = cell.icon_item.gameObject.GetComponent<Coffee.UIEffects.UIEffect>();
            if (effect != null) effect.effectFactor = 1;
            cell.icon_item.gameObject.SetActive(sprite != null);
        }
        public override void OnClose()
        {
            base.OnClose(); itemgroup_data.Clear(); _textures?.Dispose(); _textures = null;
            Singleton<FuncMgr>.Ins.GetMapData().recordFuncId = 0;
            if (_chosen >= 0) { int id = _chosen; _chosen = -1; WorldMapsRuntime.SwitchTo(id); }
        }
    }
}
