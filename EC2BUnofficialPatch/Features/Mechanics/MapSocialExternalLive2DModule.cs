using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using Config;
using EC2BUnofficialPatch.Core;
using EC2BUnofficialPatch.Workshop;
using GenUI.Common;
using HarmonyLib;
using Live2D.Cubism.Core;
using Live2D.Cubism.Framework;
using Live2D.Cubism.Framework.Json;
using Live2D.Cubism.Framework.Physics;
using Live2D.Cubism.Rendering;
using Live2D.Cubism.Rendering.Masking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sdk;
using TheEntity;
using UnityEngine;
using UnityEngine.Networking;
using View.Main;

namespace EC2BUnofficialPatch.Features.Mechanics
{
    internal sealed class MapSocialExternalLive2DModule : IPluginModule
    {
        private static readonly IReadOnlyList<ModuleLogItem> Items = new[]
        {
            new ModuleLogItem("机制", "地图社交界面外置Live2D")
        };

        public string Key => "mechanics.map-social-external-live2d";
        public IReadOnlyList<ModuleLogItem> LogItems => Items;

        public void Load(Harmony harmony, PluginServices services)
        {
            ExternalLive2DRegistry registry = ExternalLive2DRegistry.Load(services.ContentRoots);
            MapSocialExternalLive2DPatches.Initialize(registry, services.ResourceResolver);

            MethodInfo refresh = AccessTools.Method(typeof(MapRoleView), nameof(MapRoleView.Refresh), Type.EmptyTypes)
                ?? throw new MissingMethodException(typeof(MapRoleView).FullName, nameof(MapRoleView.Refresh));
            MethodInfo onExit = AccessTools.Method(typeof(MapRoleView), "OnExit", Type.EmptyTypes)
                ?? throw new MissingMethodException(typeof(MapRoleView).FullName, "OnExit");

            harmony.Patch(
                refresh,
                postfix: new HarmonyMethod(
                    typeof(MapSocialExternalLive2DPatches),
                    nameof(MapSocialExternalLive2DPatches.RefreshPostfix)));
            harmony.Patch(
                onExit,
                prefix: new HarmonyMethod(
                    typeof(MapSocialExternalLive2DPatches),
                    nameof(MapSocialExternalLive2DPatches.OnExitPrefix)));

            try
            {
                PatchLog.Registration(
                    "机制模块-地图社交界面外置Live2D已启用：" +
                    $"注册套装={registry.Count}, CubismLatestMoc={CubismMoc.LatestVersion}");
            }
            catch (Exception exception)
            {
                PatchLog.Warning(
                    "机制模块-读取游戏 Cubism Core 版本失败；模型加载时将继续由 Core 校验：" +
                    ModuleHost.GetReason(exception));
            }
        }
    }

    internal static class MapSocialExternalLive2DPatches
    {
        private static readonly FieldInfo CellField = AccessTools.Field(typeof(MapRoleView), "cell");
        private static readonly FieldInfo NpcField = AccessTools.Field(typeof(MapRoleView), "npc");
        private static readonly FieldInfo LayerOrderField = AccessTools.Field(typeof(MapRoleView), "layerOrder");

        private static ExternalLive2DRegistry _registry = ExternalLive2DRegistry.Empty;
        private static ExternalResourceResolver _resolver;

        internal static void Initialize(
            ExternalLive2DRegistry registry,
            ExternalResourceResolver resolver)
        {
            _registry = registry ?? ExternalLive2DRegistry.Empty;
            _resolver = resolver;
        }

        internal static void ReplaceRegistry(ExternalLive2DRegistry registry)
        {
            _registry = registry ?? ExternalLive2DRegistry.Empty;
        }

        public static void RefreshPostfix(MapRoleView __instance)
        {
            if (__instance == null)
                return;

            Cell_NewTalkRoleItemUI cell = GetCell(__instance);
            if (cell?.l2d_role?.gameObject == null || cell.icon_role?.gameObject == null)
                return;

            ExternalLive2DInstance instance =
                cell.l2d_role.gameObject.GetComponent<ExternalLive2DInstance>();
            // 原版 SetData 已先决定本次应显示静态图还是原版 L2D；清理旧外置实例时不得改写该状态。
            instance?.HideExternalModel();

            try
            {
                Role npc = NpcField?.GetValue(__instance) as Role;
                if (npc == null || !Cfg.PersonCfgMap.TryGetValue(npc.id, out PersonCfg personCfg))
                    return;

                int gradeState = Singleton<RoleMgr>.Ins.GetRole().GradeState;
                if (!MapRoleStaticClothPatches.IsStaticModRole(personCfg, gradeState))
                    return;

                int requestedCloth = MapRoleStaticClothPatches.GetRequestedCloth(
                    npc,
                    __instance.useSchoolCloth);
                if (!_registry.TryGet(npc.id, requestedCloth, gradeState, out ExternalLive2DEntry entry))
                    return;
                if (_resolver != null && !_resolver.IsRuntimeRootEligible(entry.RootId))
                    return;

                int layerOrder = LayerOrderField?.GetValue(__instance) is int value ? value : 200;
                if (instance == null)
                    instance = cell.l2d_role.gameObject.AddComponent<ExternalLive2DInstance>();

                instance.TryShow(cell, entry, layerOrder);
            }
            catch (Exception exception)
            {
                instance?.Clear(true);
                PatchLog.Error(
                    "机制模块-地图社交界面外置Live2D挂载失败，已回退静态立绘：" +
                    ModuleHost.GetReason(exception));
            }
        }

        public static void OnExitPrefix(MapRoleView __instance)
        {
            Cell_NewTalkRoleItemUI cell = GetCell(__instance);
            GameObject host = cell?.l2d_role?.gameObject;
            host?.GetComponent<ExternalLive2DInstance>()?.HideExternalModel();
        }

        private static Cell_NewTalkRoleItemUI GetCell(MapRoleView view)
        {
            return view == null ? null : CellField?.GetValue(view) as Cell_NewTalkRoleItemUI;
        }
    }

    internal sealed class ExternalLive2DRegistry
    {
        private const string FeatureDirectory = "ExternalLive2D";
        private const string ConfigFileName = "ExternalLive2D.json";

        private readonly Dictionary<string, ExternalLive2DEntry> _entries;
        private readonly HashSet<string> _conflicts;

        private ExternalLive2DRegistry(
            Dictionary<string, ExternalLive2DEntry> entries,
            HashSet<string> conflicts)
        {
            _entries = entries;
            _conflicts = conflicts;
        }

        internal static ExternalLive2DRegistry Empty { get; } =
            new ExternalLive2DRegistry(
                new Dictionary<string, ExternalLive2DEntry>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal));

        internal int Count => _entries.Count;

        internal static ExternalLive2DRegistry Load(ContentRootCatalog roots)
        {
            Dictionary<string, ExternalLive2DEntry> entries =
                new Dictionary<string, ExternalLive2DEntry>(StringComparer.Ordinal);
            HashSet<string> conflicts = new HashSet<string>(StringComparer.Ordinal);
            List<ExternalLive2DConfigSource> sources = new List<ExternalLive2DConfigSource>
            {
                new ExternalLive2DConfigSource(
                    "plugin-local",
                    Path.Combine(Paths.PluginPath, "EC2BUnofficialPatch", FeatureDirectory, ConfigFileName))
            };

            foreach (ContentRoot root in roots?.Roots ?? Array.Empty<ContentRoot>())
            {
                sources.Add(new ExternalLive2DConfigSource(
                    root.Id,
                    Path.Combine(root.Path, "EC2BUnofficialPatch", FeatureDirectory, ConfigFileName)));
            }

            int existingFiles = 0;
            foreach (ExternalLive2DConfigSource source in sources
                .Where(item => File.Exists(item.Path))
                .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First()))
            {
                existingFiles++;
                ExternalLive2DDocument document;
                try
                {
                    document = JsonConvert.DeserializeObject<ExternalLive2DDocument>(
                        File.ReadAllText(source.Path));
                }
                catch (Exception exception)
                {
                    PatchLog.Error(
                        $"机制模块-外置Live2D配置读取失败：path={source.Path}, " +
                        $"reason={ModuleHost.GetReason(exception)}");
                    continue;
                }

                if (document?.models == null)
                {
                    PatchLog.Error($"机制模块-外置Live2D配置缺少 models 数组：path={source.Path}");
                    continue;
                }

                foreach (ExternalLive2DConfigEntry config in document.models)
                {
                    if (!TryCreateEntry(source, config, out ExternalLive2DEntry entry, out string reason))
                    {
                        PatchLog.Error(
                            "机制模块-外置Live2D配置条目无效，已忽略：" +
                            $"path={source.Path}, personId={config?.personId ?? 0}, " +
                            $"clothId={config?.clothId ?? -1}, " +
                            $"schoolStage={config?.schoolStage ?? "all"}, reason={reason}");
                        continue;
                    }

                    string key = MakeKey(entry.PersonId, entry.ClothId, entry.SchoolStage);
                    if (conflicts.Contains(key) || entries.ContainsKey(key))
                    {
                        entries.Remove(key);
                        conflicts.Add(key);
                        PatchLog.Error(
                            "机制模块-外置Live2D角色服装重复占用，冲突项全部禁用：" +
                            $"personId={entry.PersonId}, clothId={entry.ClothId}, " +
                            $"schoolStage={entry.SchoolStage}, path={source.Path}");
                        continue;
                    }

                    entries.Add(key, entry);
                }
            }

            PatchLog.Registration(
                "机制模块-外置Live2D注册完成：" +
                $"有效套装={entries.Count}, 冲突套装={conflicts.Count}, 配置文件={existingFiles}");
            return new ExternalLive2DRegistry(entries, conflicts);
        }

        internal bool TryGet(
            int personId,
            int clothId,
            int gradeState,
            out ExternalLive2DEntry entry)
        {
            ExternalLive2DSchoolStage stage = gradeState == 0
                ? ExternalLive2DSchoolStage.Primary
                : ExternalLive2DSchoolStage.Middle;
            return _entries.TryGetValue(MakeKey(personId, clothId, stage), out entry) ||
                   _entries.TryGetValue(
                       MakeKey(personId, clothId, ExternalLive2DSchoolStage.All),
                       out entry);
        }

        private static bool TryCreateEntry(
            ExternalLive2DConfigSource source,
            ExternalLive2DConfigEntry config,
            out ExternalLive2DEntry entry,
            out string reason)
        {
            entry = null;
            reason = null;
            if (config == null || config.personId <= 0 || config.clothId < 0)
            {
                reason = "personId 必须大于0，clothId 不能为负数";
                return false;
            }

            if (!TryParseSchoolStage(config.schoolStage, out ExternalLive2DSchoolStage schoolStage))
            {
                reason = "schoolStage 只能是 all、primary 或 middle";
                return false;
            }

            string relativeModel = ExternalResourceResolver.NormalizeRelativePath(config.model);
            if (relativeModel == null ||
                !relativeModel.EndsWith(".model3.json", StringComparison.OrdinalIgnoreCase))
            {
                reason = "model 必须是相对于配置文件的 .model3.json 路径";
                return false;
            }

            string configDirectory = Path.GetDirectoryName(source.Path);
            string fullModel = Path.GetFullPath(Path.Combine(
                configDirectory,
                relativeModel.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsInside(configDirectory, fullModel))
            {
                reason = "model 路径离开了 ExternalLive2D 配置目录";
                return false;
            }

            bool hasLegacyTransform = config.scale.HasValue || config.x.HasValue || config.y.HasValue;
            bool hasAutoTransform = config.fitScale.HasValue || config.xOffset.HasValue || config.yOffset.HasValue ||
                config.staticAnchorX.HasValue || config.staticAnchorY.HasValue ||
                config.modelAnchorX.HasValue || config.modelAnchorY.HasValue;
            if (hasLegacyTransform && hasAutoTransform)
            {
                reason = "不能混用旧 scale/x/y 与自动适配或锚点字段";
                return false;
            }

            bool autoFit = !hasLegacyTransform;
            float scale = autoFit ? config.fitScale ?? 1f : config.scale ?? 1000f;
            float x = autoFit ? config.xOffset ?? 0f : config.x ?? 0f;
            float y = autoFit ? config.yOffset ?? 0f : config.y ?? 0f;
            float maximumScale = autoFit ? 20f : 100000f;
            if (!IsFinite(scale) || scale <= 0f || scale > maximumScale)
            {
                reason = autoFit
                    ? "fitScale 必须在 0 到 20 之间"
                    : "旧 scale 必须在 0 到 100000 之间";
                return false;
            }
            if (!IsFinite(x) || !IsFinite(y))
            {
                reason = autoFit
                    ? "xOffset/yOffset 必须是有限数值"
                    : "旧 x/y 必须是有限数值";
                return false;
            }

            float staticAnchorX = config.staticAnchorX ?? 0.5f;
            float staticAnchorY = config.staticAnchorY ?? 0.5f;
            float modelAnchorX = config.modelAnchorX ?? 0.5f;
            float modelAnchorY = config.modelAnchorY ?? 0.5f;
            if (!IsUnit(staticAnchorX) || !IsUnit(staticAnchorY) ||
                !IsUnit(modelAnchorX) || !IsUnit(modelAnchorY))
            {
                reason = "staticAnchorX/Y 与 modelAnchorX/Y 必须在 0 到 1 之间";
                return false;
            }

            entry = new ExternalLive2DEntry(
                config.personId,
                config.clothId,
                schoolStage,
                fullModel,
                source.RootId,
                scale,
                x,
                y,
                config.flip ?? false,
                autoFit,
                staticAnchorX,
                staticAnchorY,
                modelAnchorX,
                modelAnchorY);
            return true;
        }

        private static bool IsUnit(float value)
        {
            return IsFinite(value) && value >= 0f && value <= 1f;
        }

        private static bool TryParseSchoolStage(
            string value,
            out ExternalLive2DSchoolStage schoolStage)
        {
            string normalized = (value ?? "all").Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "":
                case "all":
                    schoolStage = ExternalLive2DSchoolStage.All;
                    return true;
                case "primary":
                    schoolStage = ExternalLive2DSchoolStage.Primary;
                    return true;
                case "middle":
                    schoolStage = ExternalLive2DSchoolStage.Middle;
                    return true;
                default:
                    schoolStage = ExternalLive2DSchoolStage.All;
                    return false;
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        internal static bool IsInside(string root, string path)
        {
            string fullRoot = Path.GetFullPath(root).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            string fullPath = Path.GetFullPath(path);
            return fullPath.StartsWith(
                fullRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        }

        private static string MakeKey(
            int personId,
            int clothId,
            ExternalLive2DSchoolStage schoolStage)
        {
            return personId + ":" + clothId + ":" + schoolStage;
        }

        private sealed class ExternalLive2DConfigSource
        {
            internal ExternalLive2DConfigSource(string rootId, string path)
            {
                RootId = rootId;
                Path = path;
            }

            internal string RootId { get; }
            internal string Path { get; }
        }

        private sealed class ExternalLive2DDocument
        {
            public List<ExternalLive2DConfigEntry> models { get; set; }
        }

        private sealed class ExternalLive2DConfigEntry
        {
            public int personId { get; set; }
            public int clothId { get; set; }
            public string schoolStage { get; set; }
            public string model { get; set; }
            public float? scale { get; set; }
            public float? x { get; set; }
            public float? y { get; set; }
            public float? fitScale { get; set; }
            public float? xOffset { get; set; }
            public float? yOffset { get; set; }
            public float? staticAnchorX { get; set; }
            public float? staticAnchorY { get; set; }
            public float? modelAnchorX { get; set; }
            public float? modelAnchorY { get; set; }
            public bool? flip { get; set; }
        }
    }

    internal enum ExternalLive2DSchoolStage
    {
        All,
        Primary,
        Middle
    }

    internal sealed class ExternalLive2DEntry
    {
        internal ExternalLive2DEntry(
            int personId,
            int clothId,
            ExternalLive2DSchoolStage schoolStage,
            string modelPath,
            string rootId,
            float scale,
            float x,
            float y,
            bool flip,
            bool autoFit,
            float staticAnchorX,
            float staticAnchorY,
            float modelAnchorX,
            float modelAnchorY)
        {
            PersonId = personId;
            ClothId = clothId;
            SchoolStage = schoolStage;
            ModelPath = modelPath;
            RootId = rootId;
            Scale = scale;
            X = x;
            Y = y;
            Flip = flip;
            AutoFit = autoFit;
            StaticAnchorX = staticAnchorX;
            StaticAnchorY = staticAnchorY;
            ModelAnchorX = modelAnchorX;
            ModelAnchorY = modelAnchorY;
        }

        internal int PersonId { get; }
        internal int ClothId { get; }
        internal ExternalLive2DSchoolStage SchoolStage { get; }
        internal string ModelPath { get; }
        internal string RootId { get; }
        internal float Scale { get; }
        internal float X { get; }
        internal float Y { get; }
        internal bool Flip { get; }
        internal bool AutoFit { get; }
        internal float StaticAnchorX { get; }
        internal float StaticAnchorY { get; }
        internal float ModelAnchorX { get; }
        internal float ModelAnchorY { get; }
    }

    internal sealed class ExternalLive2DInstance : MonoBehaviour
    {
        private const byte VisibleAlphaThreshold = 8;
        private static readonly Dictionary<int, Rect> AlphaBoundsBySprite =
            new Dictionary<int, Rect>();
        private static readonly MethodInfo MaskForceReviveMethod =
            AccessTools.Method(typeof(CubismMaskController), "ForceRevive", Type.EmptyTypes);
        private static readonly FieldInfo MaskTextureField =
            AccessTools.Field(typeof(CubismMaskController), "_maskTexture");

        private CubismModel _model;
        private CubismMoc _moc;
        private CubismRenderController _renderController;
        private ExternalLive2DEntry _entry;
        private Bounds _modelBounds;
        private bool _hasModelBounds;
        private bool _layoutApplied;
        private string _maskDiagnostics = "masks=0";
        private string _motionDiagnostics = "motionParameters=none";
        private Coroutine _loadCoroutine;
        private string _loadingModelPath;
        private readonly List<Texture2D> _textures = new List<Texture2D>();
        private Cell_NewTalkRoleItemUI _cell;

        internal bool TryShow(
            Cell_NewTalkRoleItemUI cell,
            ExternalLive2DEntry entry,
            int sortingOrder)
        {
            if (_model != null &&
                _entry != null &&
                string.Equals(_entry.ModelPath, entry.ModelPath, StringComparison.OrdinalIgnoreCase))
            {
                _cell = cell;
                _model.gameObject.SetActive(true);
                ActivateModel(entry, sortingOrder, _moc?.Version ?? 0);
                return true;
            }

            if (_loadCoroutine != null &&
                string.Equals(_loadingModelPath, entry.ModelPath, StringComparison.OrdinalIgnoreCase))
            {
                _cell = cell;
                return true;
            }

            Clear(true);
            _cell = cell;

            try
            {
                ExternalLive2DPackage package = new ExternalLive2DPackage(entry.ModelPath, _textures);
                package.Validate();
                gameObject.SetActive(true);
                _loadingModelPath = entry.ModelPath;
                _loadCoroutine = StartCoroutine(LoadAndShow(package, entry, sortingOrder));
                return true;
            }
            catch (Exception exception)
            {
                PatchLog.Error(
                    "机制模块-外置Live2D加载失败，已回退当前服装的静态立绘：" +
                    $"personId={entry.PersonId}, clothId={entry.ClothId}, path={entry.ModelPath}, " +
                    $"reason={ModuleHost.GetReason(exception)}");
                Clear(true);
                return false;
            }
        }

        private IEnumerator LoadAndShow(
            ExternalLive2DPackage package,
            ExternalLive2DEntry entry,
            int sortingOrder)
        {
            yield return package.PreloadTexturesAsync();
            _loadCoroutine = null;
            _loadingModelPath = null;

            try
            {
                if (!string.IsNullOrEmpty(package.LoadError))
                    throw new InvalidDataException(package.LoadError);

                CubismModel3Json modelJson = CubismModel3Json.LoadAtPath(
                    entry.ModelPath,
                    package.LoadAsset);
                if (modelJson == null)
                    throw new InvalidDataException("Cubism SDK 无法解析 model3.json");

                _model = modelJson.ToModel(true);
                if (_model == null)
                    throw new InvalidDataException("Cubism SDK 无法从 moc3 创建模型");
                package.InitializePhysics(_model);

                _moc = _model.Moc;
                uint latestVersion = CubismMoc.LatestVersion;
                uint modelVersion = _moc?.Version ?? 0;
                if (modelVersion == 0 || modelVersion > latestVersion)
                {
                    throw new NotSupportedException(
                        $"moc3 格式不受当前 Core 支持：model={modelVersion}, latest={latestVersion}");
                }

                Transform modelTransform = _model.transform;
                modelTransform.SetParent(transform, false);
                modelTransform.localPosition = Vector3.zero;
                modelTransform.localRotation = Quaternion.identity;
                modelTransform.localScale = Vector3.one;

                _renderController = _model.GetComponent<CubismRenderController>();
                if (_renderController == null || _renderController.Renderers == null)
                    throw new InvalidDataException("模型缺少 Cubism 渲染控制器");
                if (_renderController.Renderers.Any(renderer => renderer == null || renderer.MainTexture == null))
                    throw new InvalidDataException("模型存在未绑定纹理的 Drawable");

                _renderController.SortingOrder = sortingOrder;
                _model.ForceUpdateNow();
                ConfigureMasking(_model);
                _modelBounds = CalculateModelBounds(_model, _renderController.Renderers);
                _hasModelBounds = _modelBounds.size.y > 0.0001f;
                if (!_hasModelBounds)
                    throw new InvalidDataException("无法取得模型 Drawable 的有效显示范围");

                ExternalLive2DMotionDriver motionDriver =
                    _model.gameObject.AddComponent<ExternalLive2DMotionDriver>();
                motionDriver.Initialize(_model);
                _motionDiagnostics = motionDriver.Diagnostics;
                _model.gameObject.GetComponent<CubismUpdateController>()?.Refresh();

                ActivateModel(entry, sortingOrder, modelVersion);
            }
            catch (Exception exception)
            {
                PatchLog.Error(
                    "机制模块-外置Live2D加载失败，已回退当前服装的静态立绘：" +
                    $"personId={entry.PersonId}, clothId={entry.ClothId}, path={entry.ModelPath}, " +
                    $"reason={ModuleHost.GetReason(exception)}");
                Clear(true);
            }
        }

        private void Update()
        {
            if (_model != null && _entry?.AutoFit == true)
                TryApplyAutoLayout(_moc?.Version ?? 0);
        }

        private void ActivateModel(ExternalLive2DEntry entry, int sortingOrder, uint modelVersion)
        {
            _entry = entry;
            _layoutApplied = false;
            _renderController.SortingOrder = sortingOrder;
            _renderController.Opacity = entry.AutoFit ? 0f : 1f;
            gameObject.SetActive(true);

            if (entry.AutoFit)
            {
                TryApplyAutoLayout(modelVersion);
                return;
            }

            ApplyLegacyLayout(entry);
            CompleteShow(
                modelVersion,
                $"legacyScale={entry.Scale:F3}, legacyX={entry.X:F1}, legacyY={entry.Y:F1}");
        }

        private void ApplyLegacyLayout(ExternalLive2DEntry entry)
        {
            float signedScale = entry.Flip ? -entry.Scale : entry.Scale;
            _model.transform.localScale = new Vector3(signedScale, entry.Scale, entry.Scale);
            _model.transform.localPosition = new Vector3(entry.X, entry.Y, 0f);
            _layoutApplied = true;
        }

        private bool TryApplyAutoLayout(uint modelVersion)
        {
            RectTransform source = _cell?.icon_role?.transform;
            if (source == null || _cell.icon_role.image?.sprite == null || !_hasModelBounds)
                return false;

            Rect fullRect = source.rect;
            if (fullRect.height <= 1f)
                return false;

            Rect visibleRect = GetVisiblePortraitRect(_cell.icon_role.image.sprite, fullRect);

            Vector3 anchor = transform.InverseTransformPoint(source.TransformPoint(new Vector3(
                Mathf.Lerp(visibleRect.xMin, visibleRect.xMax, _entry.StaticAnchorX),
                Mathf.Lerp(visibleRect.yMin, visibleRect.yMax, _entry.StaticAnchorY),
                0f)));
            Vector3 bottom = transform.InverseTransformPoint(
                source.TransformPoint(new Vector3(visibleRect.center.x, visibleRect.yMin, 0f)));
            Vector3 top = transform.InverseTransformPoint(
                source.TransformPoint(new Vector3(visibleRect.center.x, visibleRect.yMax, 0f)));
            float targetHeight = Vector3.Distance(bottom, top);
            if (targetHeight <= 1f)
                return false;

            float scale = targetHeight / _modelBounds.size.y * _entry.Scale;
            float signedScale = _entry.Flip ? -scale : scale;
            float modelAnchorX = Mathf.Lerp(_modelBounds.min.x, _modelBounds.max.x, _entry.ModelAnchorX);
            float modelAnchorY = Mathf.Lerp(_modelBounds.min.y, _modelBounds.max.y, _entry.ModelAnchorY);
            _model.transform.localScale = new Vector3(signedScale, scale, scale);
            _model.transform.localPosition = new Vector3(
                anchor.x - modelAnchorX * signedScale + _entry.X,
                anchor.y - modelAnchorY * scale + _entry.Y,
                0f);

            if (!_layoutApplied)
            {
                _layoutApplied = true;
                CompleteShow(
                    modelVersion,
                    $"autoFitVisibleHeight={targetHeight:F1}, modelHeight={_modelBounds.size.y:F3}, " +
                    $"fitScale={_entry.Scale:F3}, effectiveScale={scale:F3}, " +
                    $"anchors={_entry.StaticAnchorX:F2},{_entry.StaticAnchorY:F2}->" +
                    $"{_entry.ModelAnchorX:F2},{_entry.ModelAnchorY:F2}, " +
                    $"xOffset={_entry.X:F1}, yOffset={_entry.Y:F1}");
            }
            return true;
        }

        private void CompleteShow(uint modelVersion, string layout)
        {
            _renderController.Opacity = 1f;
            _cell.icon_role.gameObject.SetActive(false);
            ExternalLive2DLog.SuccessOnce(
                _entry.ModelPath,
                "机制模块-外置Live2D显示成功：" +
                $"personId={_entry.PersonId}, clothId={_entry.ClothId}, " +
                $"schoolStage={_entry.SchoolStage}, " +
                $"mocVersion={modelVersion}, textures={_textures.Count}, {_maskDiagnostics}, " +
                $"{_motionDiagnostics}, {layout}, path={_entry.ModelPath}");
        }

        private void ConfigureMasking(CubismModel model)
        {
            string[] maskSets = model.Drawables
                .Where(drawable => drawable != null && drawable.IsMasked)
                .Select(drawable => string.Join(",", (drawable.Masks ?? Array.Empty<CubismDrawable>())
                    .Where(mask => mask != null)
                    .Select(mask => mask.Id)
                    .OrderBy(id => id, StringComparer.Ordinal)))
                .Where(value => !string.IsNullOrEmpty(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (maskSets.Length == 0)
            {
                _maskDiagnostics = "masks=0";
                return;
            }

            CubismMaskController controller = model.GetComponent<CubismMaskController>();
            if (controller == null)
                throw new InvalidDataException("模型声明了遮罩，但 CubismMaskController 未初始化");

            if (MaskForceReviveMethod == null || MaskTextureField == null)
                throw new MissingMethodException(typeof(CubismMaskController).FullName, "ForceRevive");

            CubismMaskTexture globalMaskTexture = CubismMaskTexture.GlobalMaskTexture;
            if (globalMaskTexture == null)
                throw new InvalidDataException("游戏 Cubism GlobalMaskTexture 资源不可用");

            BindMaskTexture(controller, globalMaskTexture);
            _maskDiagnostics =
                $"masks={maskSets.Length}, maskTexture=game-global, " +
                $"maskCommands={controller.CountOfCommandBuffers}";
        }

        private static void BindMaskTexture(
            CubismMaskController controller,
            CubismMaskTexture maskTexture)
        {
            // MaskTexture 属性 Setter 会立即手工调用 OnEnable，从而在旧 Junction 上分配 Tile；
            // 随后的 ForceRevive 会替换 Junction，而重复 Source 又阻止新 Tile 分配。
            // 绕开 Setter 后，顺序严格为：停用并移除旧 Source -> 写字段 -> 重建 Junction
            // -> 启用并首次 AddSource，此时 ReinitializeSources 会给新 Junction 分配 Tile。
            if (controller.enabled)
                controller.enabled = false;
            MaskTextureField.SetValue(controller, maskTexture);
            MaskForceReviveMethod.Invoke(controller, null);
            controller.enabled = true;
            controller.OnLateUpdate();

            if (!ReferenceEquals(controller.MaskTexture, maskTexture))
            {
                throw new InvalidDataException("Cubism 遮罩控制器重绑定后未保留目标纹理");
            }
        }

        private static Rect GetVisiblePortraitRect(Sprite sprite, Rect targetRect)
        {
            if (sprite == null || sprite.texture == null)
                return targetRect;

            int key = sprite.GetInstanceID();
            if (!AlphaBoundsBySprite.TryGetValue(key, out Rect normalized))
            {
                normalized = CalculateNormalizedAlphaBounds(sprite);
                AlphaBoundsBySprite[key] = normalized;
            }

            return Rect.MinMaxRect(
                Mathf.Lerp(targetRect.xMin, targetRect.xMax, normalized.xMin),
                Mathf.Lerp(targetRect.yMin, targetRect.yMax, normalized.yMin),
                Mathf.Lerp(targetRect.xMin, targetRect.xMax, normalized.xMax),
                Mathf.Lerp(targetRect.yMin, targetRect.yMax, normalized.yMax));
        }

        private static Rect CalculateNormalizedAlphaBounds(Sprite sprite)
        {
            try
            {
                Texture2D texture = sprite.texture;
                Color32[] pixels = texture.GetPixels32();
                Rect textureRect = sprite.textureRect;
                int xMin = Mathf.Clamp(Mathf.FloorToInt(textureRect.xMin), 0, texture.width - 1);
                int yMin = Mathf.Clamp(Mathf.FloorToInt(textureRect.yMin), 0, texture.height - 1);
                int xMax = Mathf.Clamp(Mathf.CeilToInt(textureRect.xMax) - 1, xMin, texture.width - 1);
                int yMax = Mathf.Clamp(Mathf.CeilToInt(textureRect.yMax) - 1, yMin, texture.height - 1);
                int visibleMinY = FindVisibleRow(pixels, texture.width, xMin, xMax, yMin, yMax, 1);
                if (visibleMinY < 0 || textureRect.width <= 0f || textureRect.height <= 0f)
                    return new Rect(0f, 0f, 1f, 1f);

                int visibleMaxY = FindVisibleRow(pixels, texture.width, xMin, xMax, yMax, visibleMinY, -1);
                int visibleMinX = FindVisibleColumn(
                    pixels, texture.width, visibleMinY, visibleMaxY, xMin, xMax, 1);
                int visibleMaxX = FindVisibleColumn(
                    pixels, texture.width, visibleMinY, visibleMaxY, xMax, visibleMinX, -1);

                return Rect.MinMaxRect(
                    Mathf.Clamp01((visibleMinX - textureRect.xMin) / textureRect.width),
                    Mathf.Clamp01((visibleMinY - textureRect.yMin) / textureRect.height),
                    Mathf.Clamp01((visibleMaxX + 1f - textureRect.xMin) / textureRect.width),
                    Mathf.Clamp01((visibleMaxY + 1f - textureRect.yMin) / textureRect.height));
            }
            catch
            {
                // 某些打包 Sprite 不允许读取像素；此时安全退回完整 Rect，仍可用锚点和偏移校准。
                return new Rect(0f, 0f, 1f, 1f);
            }
        }

        private static int FindVisibleRow(
            Color32[] pixels,
            int textureWidth,
            int xMin,
            int xMax,
            int start,
            int end,
            int step)
        {
            for (int y = start; step > 0 ? y <= end : y >= end; y += step)
            {
                int row = y * textureWidth;
                for (int x = xMin; x <= xMax; x++)
                {
                    if (pixels[row + x].a > VisibleAlphaThreshold)
                        return y;
                }
            }
            return -1;
        }

        private static int FindVisibleColumn(
            Color32[] pixels,
            int textureWidth,
            int yMin,
            int yMax,
            int start,
            int end,
            int step)
        {
            for (int x = start; step > 0 ? x <= end : x >= end; x += step)
            {
                for (int y = yMin; y <= yMax; y++)
                {
                    if (pixels[y * textureWidth + x].a > VisibleAlphaThreshold)
                        return x;
                }
            }
            return -1;
        }

        private static Bounds CalculateModelBounds(
            CubismModel model,
            IEnumerable<CubismRenderer> renderers)
        {
            Bounds result = default(Bounds);
            bool initialized = false;
            Matrix4x4 worldToModel = model.transform.worldToLocalMatrix;
            foreach (CubismRenderer renderer in renderers)
            {
                Mesh mesh = renderer?.Mesh;
                if (mesh == null || renderer.Color.a <= 0.001f)
                    continue;

                Bounds bounds = mesh.bounds;
                Matrix4x4 toModel = worldToModel * renderer.transform.localToWorldMatrix;
                for (int x = -1; x <= 1; x += 2)
                {
                    for (int y = -1; y <= 1; y += 2)
                    {
                        for (int z = -1; z <= 1; z += 2)
                        {
                            Vector3 corner = bounds.center + Vector3.Scale(
                                bounds.extents,
                                new Vector3(x, y, z));
                            Vector3 point = toModel.MultiplyPoint3x4(corner);
                            if (!initialized)
                            {
                                result = new Bounds(point, Vector3.zero);
                                initialized = true;
                            }
                            else
                            {
                                result.Encapsulate(point);
                            }
                        }
                    }
                }
            }
            return result;
        }

        internal void Clear(bool restoreStatic)
        {
            if (_loadCoroutine != null)
            {
                StopCoroutine(_loadCoroutine);
                _loadCoroutine = null;
                _loadingModelPath = null;
            }

            if (_model != null)
            {
                _model.gameObject.SetActive(false);
                Destroy(_model.gameObject);
                _model = null;
            }

            if (_moc != null)
            {
                Destroy(_moc);
                _moc = null;
            }

            foreach (Texture2D texture in _textures)
            {
                if (texture != null)
                    Destroy(texture);
            }
            _textures.Clear();
            _renderController = null;
            _entry = null;
            _hasModelBounds = false;
            _layoutApplied = false;
            _maskDiagnostics = "masks=0";
            _motionDiagnostics = "motionParameters=none";

            if (restoreStatic && _cell?.icon_role?.gameObject != null)
            {
                _cell.icon_role.gameObject.SetActive(true);
                gameObject.SetActive(false);
            }

            _cell = restoreStatic ? _cell : null;
        }

        internal void HideExternalModel()
        {
            if (_loadCoroutine != null)
            {
                StopCoroutine(_loadCoroutine);
                _loadCoroutine = null;
                _loadingModelPath = null;
                foreach (Texture2D texture in _textures)
                {
                    if (texture != null)
                        Destroy(texture);
                }
                _textures.Clear();
            }
            if (_model != null)
                _model.gameObject.SetActive(false);
            _layoutApplied = false;
        }

        private void OnDestroy()
        {
            Clear(false);
        }
    }

    internal sealed class ExternalLive2DMotionDriver : MonoBehaviour, ICubismUpdatable
    {
        private readonly Dictionary<string, CubismParameter> _parameters =
            new Dictionary<string, CubismParameter>(StringComparer.Ordinal);
        private float _lookX;
        private float _lookY;

        public int ExecutionOrder => 750;
        public bool NeedsUpdateOnEditing => false;
        public bool HasUpdateController { get; set; }
        internal string Diagnostics { get; private set; } = "motionParameters=none";

        internal void Initialize(CubismModel model)
        {
            _parameters.Clear();
            foreach (CubismParameter parameter in model?.Parameters ?? Array.Empty<CubismParameter>())
            {
                if (parameter != null && !string.IsNullOrEmpty(parameter.Id))
                    _parameters[parameter.Id] = parameter;
            }

            string[] driven =
            {
                "ParamAngleX", "ParamAngleY", "ParamAngleZ",
                "ParamBodyAngleX", "ParamBodyAngleZ",
                "ParamEyeBallX", "ParamEyeBallY",
                "ParamEyeLOpen", "ParamEyeROpen", "ParamBreath"
            };
            string[] available = driven.Where(id => _parameters.ContainsKey(id)).ToArray();
            Diagnostics = available.Length == 0
                ? "motionParameters=none"
                : "motionParameters=" + string.Join(",", available);
        }

        public void OnLateUpdate()
        {
            float delta = Mathf.Max(0f, Time.unscaledDeltaTime);
            Vector2 mouse = ReadNormalizedMousePosition();
            float smoothing = 1f - Mathf.Exp(-10f * delta);
            _lookX = Mathf.Lerp(_lookX, mouse.x, smoothing);
            _lookY = Mathf.Lerp(_lookY, mouse.y, smoothing);

            float time = Time.unscaledTime;
            float idle = Mathf.Sin(time * 0.75f);
            float slowIdle = Mathf.Sin(time * 0.43f + 0.8f);
            float breath = 0.5f + 0.5f * Mathf.Sin(time * 1.55f);

            SetSigned("ParamAngleX", _lookX * 0.75f + idle * 0.05f);
            SetSigned("ParamAngleY", _lookY * 0.65f + slowIdle * 0.04f);
            SetSigned("ParamAngleZ", -_lookX * 0.08f + idle * 0.08f);
            SetSigned("ParamBodyAngleX", _lookX * 0.12f + slowIdle * 0.08f);
            SetSigned("ParamBodyAngleZ", -_lookX * 0.04f + idle * 0.06f);
            SetSigned("ParamEyeBallX", _lookX * 0.9f);
            SetSigned("ParamEyeBallY", _lookY * 0.75f);
            SetUnit("ParamBreath", breath);

            float blinkPhase = Mathf.Repeat(time, 4.2f);
            float eyeOpen = blinkPhase < 0.18f
                ? Mathf.Abs(blinkPhase / 0.18f * 2f - 1f)
                : 1f;
            SetUnit("ParamEyeLOpen", eyeOpen);
            SetUnit("ParamEyeROpen", eyeOpen);
        }

        private static Vector2 ReadNormalizedMousePosition()
        {
            if (Screen.width <= 0 || Screen.height <= 0)
                return Vector2.zero;

            try
            {
                Vector2 position = Control.mousePosition;
                return new Vector2(
                    Mathf.Clamp(position.x / Screen.width * 2f - 1f, -1f, 1f),
                    Mathf.Clamp(position.y / Screen.height * 2f - 1f, -1f, 1f));
            }
            catch
            {
                return Vector2.zero;
            }
        }

        private void SetSigned(string id, float normalized)
        {
            if (!_parameters.TryGetValue(id, out CubismParameter parameter))
                return;

            float amount = Mathf.Clamp(normalized, -1f, 1f);
            float value = amount >= 0f
                ? Mathf.Lerp(parameter.DefaultValue, parameter.MaximumValue, amount)
                : Mathf.Lerp(parameter.DefaultValue, parameter.MinimumValue, -amount);
            parameter.Value = Mathf.Clamp(value, parameter.MinimumValue, parameter.MaximumValue);
        }

        private void SetUnit(string id, float normalized)
        {
            if (!_parameters.TryGetValue(id, out CubismParameter parameter))
                return;

            parameter.Value = Mathf.Lerp(
                parameter.MinimumValue,
                parameter.MaximumValue,
                Mathf.Clamp01(normalized));
        }
    }

    internal sealed class ExternalLive2DPackage
    {
        private readonly string _modelPath;
        private readonly string _modelRoot;
        private readonly List<Texture2D> _ownedTextures;
        private readonly Dictionary<string, byte[]> _bytes =
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _texts =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Texture2D> _preloadedTextures =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _texturePaths = new List<string>();
        private string _physicsPath;

        internal string LoadError { get; private set; }

        internal ExternalLive2DPackage(string modelPath, List<Texture2D> ownedTextures)
        {
            _modelPath = Path.GetFullPath(modelPath);
            _modelRoot = Path.GetDirectoryName(_modelPath);
            _ownedTextures = ownedTextures;
        }

        internal void Validate()
        {
            if (!File.Exists(_modelPath))
                throw new FileNotFoundException("找不到 model3.json", _modelPath);

            JObject json = JObject.Parse(ReadText(_modelPath));
            if ((int?)json["Version"] != 3)
                throw new InvalidDataException("model3.json Version 必须为 3");

            JObject references = json["FileReferences"] as JObject;
            string moc = (string)references?["Moc"];
            JArray textures = references?["Textures"] as JArray;
            if (string.IsNullOrWhiteSpace(moc) || textures == null || textures.Count == 0)
                throw new InvalidDataException("model3.json 必须声明 Moc 和至少一张纹理");

            string mocPath = ResolveReference(moc, ".moc3");
            byte[] mocBytes = ReadBytes(mocPath);
            if (!CubismMoc.HasMocConsistency(mocBytes))
                throw new InvalidDataException("moc3 一致性校验失败");

            foreach (JToken token in textures)
            {
                _texturePaths.Add(ResolveReference((string)token, ".png"));
            }

            string physics = (string)references["Physics"];
            if (!string.IsNullOrWhiteSpace(physics))
            {
                _physicsPath = ResolveReference(physics, ".physics3.json");
                JObject.Parse(ReadText(_physicsPath));
            }
        }

        internal IEnumerator PreloadTexturesAsync()
        {
            foreach (string path in _texturePaths)
            {
                using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(
                    new Uri(path).AbsoluteUri,
                    true))
                {
                    yield return request.SendWebRequest();
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        LoadError = "PNG 异步加载失败：" + path + "，" + request.error;
                        yield break;
                    }

                    Texture2D texture = DownloadHandlerTexture.GetContent(request);
                    if (texture == null)
                    {
                        LoadError = "PNG 异步解码结果为空：" + path;
                        yield break;
                    }

                    texture.name = Path.GetFileNameWithoutExtension(path);
                    texture.wrapMode = TextureWrapMode.Clamp;
                    _preloadedTextures[path] = texture;
                    _ownedTextures.Add(texture);
                }
            }
        }

        internal void InitializePhysics(CubismModel model)
        {
            if (string.IsNullOrEmpty(_physicsPath))
                return;

            CubismPhysics3Json physicsJson = CubismPhysics3Json.LoadFrom(
                ReadText(_physicsPath));
            if (physicsJson == null)
                throw new InvalidDataException("Cubism SDK 无法解析 physics3.json");

            CubismPhysicsController controller =
                model.gameObject.GetComponent<CubismPhysicsController>() ??
                model.gameObject.AddComponent<CubismPhysicsController>();
            controller.Initialize(physicsJson.ToRig());
            model.gameObject.GetComponent<CubismUpdateController>()?.Refresh();
        }

        internal object LoadAsset(Type assetType, string assetPath)
        {
            if (assetType == null || string.IsNullOrWhiteSpace(assetPath))
                return null;

            string fullPath = ResolveSdkPath(assetPath);
            if (fullPath == null || !File.Exists(fullPath))
                return null;

            if (assetType == typeof(string))
            {
                // 物理在模型完整创建后单独初始化，确保失败时本模块能回收已创建实例。
                if (!string.IsNullOrEmpty(_physicsPath) &&
                    string.Equals(fullPath, _physicsPath, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                return ReadText(fullPath);
            }
            if (assetType == typeof(byte[]))
                return ReadBytes(fullPath);
            if (assetType != typeof(Texture2D))
                return null;

            if (!string.Equals(Path.GetExtension(fullPath), ".png", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Cubism 纹理仅允许 PNG：" + fullPath);

            if (_preloadedTextures.TryGetValue(fullPath, out Texture2D preloaded))
                return preloaded;

            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = Path.GetFileNameWithoutExtension(fullPath),
                wrapMode = TextureWrapMode.Clamp
            };
            if (!ImageConversion.LoadImage(texture, ReadBytes(fullPath), true))
            {
                DestroyObject(texture);
                throw new InvalidDataException("PNG 纹理解码失败：" + fullPath);
            }

            _ownedTextures.Add(texture);
            return texture;
        }

        private byte[] ReadBytes(string path)
        {
            if (!_bytes.TryGetValue(path, out byte[] value))
            {
                value = File.ReadAllBytes(path);
                _bytes.Add(path, value);
            }
            return value;
        }

        private string ReadText(string path)
        {
            if (!_texts.TryGetValue(path, out string value))
            {
                value = File.ReadAllText(path);
                _texts.Add(path, value);
            }
            return value;
        }

        private string ResolveReference(string relativePath, string requiredSuffix)
        {
            string normalized = ExternalResourceResolver.NormalizeRelativePath(relativePath);
            if (normalized == null ||
                !normalized.EndsWith(requiredSuffix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"非法模型引用，必须是模型目录内的 {requiredSuffix} 相对路径：{relativePath}");
            }

            string fullPath = Path.GetFullPath(Path.Combine(
                _modelRoot,
                normalized.Replace('/', Path.DirectorySeparatorChar)));
            if (!ExternalLive2DRegistry.IsInside(_modelRoot, fullPath))
                throw new InvalidDataException("模型引用离开模型根目录：" + relativePath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("模型引用的文件不存在", fullPath);
            return fullPath;
        }

        private string ResolveSdkPath(string assetPath)
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(assetPath.Replace('/', Path.DirectorySeparatorChar));
            }
            catch
            {
                return null;
            }

            return ExternalLive2DRegistry.IsInside(_modelRoot, fullPath) ||
                   string.Equals(fullPath, _modelPath, StringComparison.OrdinalIgnoreCase)
                ? fullPath
                : null;
        }

        private static void DestroyObject(UnityEngine.Object value)
        {
            if (value != null)
                UnityEngine.Object.Destroy(value);
        }
    }

    internal static class ExternalLive2DLog
    {
        private static readonly HashSet<string> SuccessfulModels =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal static void SuccessOnce(string modelPath, string message)
        {
            if (SuccessfulModels.Add(modelPath ?? string.Empty))
                PatchLog.Info(message);
        }
    }
}
