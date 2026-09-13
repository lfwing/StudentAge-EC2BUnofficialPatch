using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using Effect;
using HarmonyLib;
using LFBetterAudio.Assets;
using LFBetterAudio.Config;
using LFBetterAudio.Discovery;
using LFBetterAudio.Effects;
using LFBetterAudio.Timeline;
using LFBetterAudio.Patches;
using LFBetterAudio.Runtime;
using LFBetterAudio.Templates;
using UnityEngine;
using View.Evt;

namespace LFBetterAudio
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal const string PluginGuid = "sa.lf.betteraudio";
        internal const string PluginName = "LFBetterAudio";
        internal const string PluginVersion = "1.0.0";

        internal static Plugin Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }
        internal static string TemplateDirectory { get; private set; }
        internal static string RuntimeAssetDirectory { get; private set; }
        internal static BetterAudioConfigStore ConfigStore { get; private set; }

        private const float PatchHealthCheckInterval = 1f;

        private Harmony _harmony;
        private MethodInfo _normalFactoryTarget;
        private MethodInfo _normalFactoryPrefix;
        private MethodInfo _runtimeTextStartTarget;
        private MethodInfo _runtimeTextStartPrefix;
        private MethodInfo _previewTextStartTarget;
        private MethodInfo _previewTextStartPrefix;
        private MethodInfo _previewRefreshTarget;
        private MethodInfo _previewRefreshPrefix;

        private float _nextPatchHealthCheckTime;
        private bool _isRepairingCriticalPatches;
        private bool _applicationQuitting;
        private bool _startupSelfCheckFinished;
        private readonly List<string> _startupIssues = new List<string>();

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            try
            {
                TemplateDirectory = Path.GetFullPath(
                    Path.Combine(Paths.PluginPath, ModAuthorTemplateInstaller.TemplateFolderName));
                RuntimeAssetDirectory = Path.GetFullPath(
                    Path.Combine(Paths.CachePath, "LFBetterAudio", "RuntimeAssets"));

                bool loadedNormally = true;
                _harmony = new Harmony(PluginGuid);

                // 1163 工厂、正常游戏文字开始、Preview 文字开始及 Preview 空文本入口
                // 采用手动精确安装，便于启动自检及持久控制器补装。
                loadedNormally &= TryInitialize(
                    "1163关键入口",
                    () =>
                    {
                        ResolveCriticalPatchMethods();
                        EnsureCriticalPatchesInstalled();
                        VerifyCriticalEffectPaths();
                    });

                loadedNormally &= TryInitialize(
                    "Mod作者模板",
                    () => ModAuthorTemplateInstaller.EnsureInstalled(TemplateDirectory));

                loadedNormally &= TryInitialize(
                    "内置校验资源",
                    () =>
                    {
                        BuiltInValidationAssets.Initialize(RuntimeAssetDirectory);
                        if (!BuiltInValidationAssets.IsReady ||
                            !BuiltInValidationAssets.TryGetEntry(
                                BuiltInValidationAssets.ValidationMusicId,
                                out BetterAudioEntry validationEntry) ||
                            validationEntry == null ||
                            validationEntry.Name != "校验音乐")
                        {
                            throw new InvalidOperationException(
                                "内置校验音乐 1163001 未正确注册为“校验音乐”。");
                        }
                    });

                loadedNormally &= TryInitialize(
                    "BetterAudio资源包",
                    LoadMusicPackages);

                loadedNormally &= TryInitialize(
                    "播放控制器",
                    () =>
                    {
                        if (BetterAudioController.EnsureInstance() == null)
                        {
                            throw new InvalidOperationException("无法创建 BetterAudioController。");
                        }
                    });

                loadedNormally &= TryInitialize(
                    "浮动歌词组件",
                    ValidateLyricsVisualComponents);

                loadedNormally &= TryInitialize(
                    "Harmony外围补丁",
                    () =>
                    {
                        // The merged assembly also contains UP modules, which own their own Harmony IDs.
                        foreach (Type patchType in typeof(Plugin).Assembly.GetTypes()
                            .Where(type => type.Namespace == "LFBetterAudio.Patches" &&
                                type.IsDefined(typeof(HarmonyPatch), false)))
                            _harmony.CreateClassProcessor(patchType).Patch();
                    });

                // PatchAll 后再次确认，避免补丁排序或其他插件初始化改变关键入口。
                loadedNormally &= TryInitialize(
                    "1163关键入口复检",
                    () =>
                    {
                        EnsureCriticalPatchesInstalled();
                        VerifyCriticalEffectPaths();
                    });

                _nextPatchHealthCheckTime = Time.unscaledTime + PatchHealthCheckInterval;

                int totalMusicCount = (ConfigStore?.Count ?? 0) +
                                      (BuiltInValidationAssets.IsReady ? 1 : 0);
                CompleteStartupSelfCheck(loadedNormally, totalMusicCount);
            }
            catch (Exception ex)
            {
                AddStartupIssue($"启动流程：{ex.Message}");
                int totalMusicCount = (ConfigStore?.Count ?? 0) +
                                      (BuiltInValidationAssets.IsReady ? 1 : 0);
                CompleteStartupSelfCheck(false, totalMusicCount);
            }
        }

        private void Update()
        {
            TickPatchHealthFromPersistentController();
        }

        private void LoadMusicPackages()
        {
            IReadOnlyList<BetterAudioPackage> packages =
                BetterAudioPackageDiscovery.DiscoverWorkshopPackages(Paths.GameRootPath);

            ConfigStore = BetterAudioConfigStore.LoadAll(packages);
        }

        private bool TryInitialize(string moduleName, Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception ex)
            {
                AddStartupIssue($"{moduleName}：{ex.Message}");
                if (moduleName == "BetterAudio资源包")
                {
                    ConfigStore = BetterAudioConfigStore.CreateEmpty();
                }
                return false;
            }
        }

        internal static void ReportStartupIssue(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            Plugin instance = Instance;
            if (instance != null && !instance._startupSelfCheckFinished)
            {
                instance.AddStartupIssue(message);
            }
        }

        internal static void LogEffectSuccess(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                Log?.LogInfo($"[1163执行成功] {message}");
            }
        }

        internal static void LogEffectError(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                Log?.LogError($"[1163错误] {message}");
            }
        }

        internal static void LogEffectError(string message, IReadOnlyList<float> rawEffect)
        {
            string raw = rawEffect == null
                ? string.Empty
                : $"；指令={BetterAudioEffectEncoding.Format(rawEffect)}";
            LogEffectError((message ?? "未知错误") + raw);
        }

        private void AddStartupIssue(string issue)
        {
            if (string.IsNullOrWhiteSpace(issue))
            {
                return;
            }

            string normalized = issue.Trim();
            if (!_startupIssues.Contains(normalized))
            {
                _startupIssues.Add(normalized);
            }
        }

        private void CompleteStartupSelfCheck(bool loadedNormally, int totalMusicCount)
        {
            _startupSelfCheckFinished = true;
            int packageCount = ConfigStore?.PackageCount ?? 0;

            if (loadedNormally && _startupIssues.Count == 0)
            {
                Log?.LogInfo(
                    $"[启动自检成功] BetterAudio {PluginVersion} 全流程自检通过；" +
                    $"音频条目={totalMusicCount}，资源包={packageCount}。");
                return;
            }

            string detail = _startupIssues.Count == 0
                ? "存在未明确归类的初始化异常。"
                : string.Join("；", _startupIssues);
            Log?.LogError(
                $"[启动自检失败] BetterAudio {PluginVersion} 全流程自检未通过：{detail}");
        }

        private static void ValidateLyricsVisualComponents()
        {
            GameObject probeObject = null;
            try
            {
                probeObject = new GameObject(
                    "lf-BetterAudioLyricsSelfCheck",
                    typeof(RectTransform),
                    typeof(CanvasGroup),
                    typeof(UnityEngine.UI.Image),
                    typeof(UnityEngine.EventSystems.EventTrigger),
                    typeof(LyricsContrastProbe));

                if (probeObject.GetComponent<LyricsContrastProbe>() == null)
                {
                    throw new InvalidOperationException("自动描边采样组件无法创建。");
                }

                if (probeObject.GetComponent<UnityEngine.EventSystems.EventTrigger>() == null ||
                    probeObject.GetComponent<UnityEngine.UI.Image>() == null)
                {
                    throw new InvalidOperationException("浮动歌词拖动或菜单交互组件无法创建。");
                }

                var state = new FloatingLyricsRuntimeState(1, 0, false);
                state.SetRuntimeSize(4);
                state.SetRuntimeColor(31);
                var positionLock = new FloatingLyricsPositionLockState();
                positionLock.Lock(new Vector2(10f, -20f));
                if (state.EffectiveSizeMode != 4 || !state.HasColorOverride ||
                    !positionLock.IsLocked)
                {
                    throw new InvalidOperationException("浮动歌词跨 Talk 状态组件自检失败。");
                }
            }
            finally
            {
                if (probeObject != null)
                {
                    UnityEngine.Object.Destroy(probeObject);
                }
            }
        }

        private void ResolveCriticalPatchMethods()
        {
            _normalFactoryTarget = AccessTools.Method(
                typeof(CommonEvtMgr),
                nameof(CommonEvtMgr.GenEffector),
                new[]
                {
                    typeof(List<float>),
                    typeof(Effector),
                    typeof(int),
                    typeof(int)
                });
            _normalFactoryPrefix = AccessTools.Method(
                typeof(GenEffector1163Patch),
                nameof(GenEffector1163Patch.Prefix));

            _runtimeTextStartTarget = AccessTools.Method(
                typeof(NewTalkView),
                nameof(NewTalkView.DoText),
                new[] { typeof(string) });
            _runtimeTextStartPrefix = AccessTools.Method(
                typeof(RuntimeDoTextEffectPatch),
                nameof(RuntimeDoTextEffectPatch.Prefix));

            _previewTextStartTarget = AccessTools.Method(
                typeof(PreviewTalkView),
                nameof(PreviewTalkView.DoText),
                new[] { typeof(string) });
            _previewTextStartPrefix = AccessTools.Method(
                typeof(PreviewDoTextEffectPatch),
                nameof(PreviewDoTextEffectPatch.Prefix));

            _previewRefreshTarget = AccessTools.Method(
                typeof(PreviewTalkView),
                nameof(PreviewTalkView.RefreshTalk),
                new[] { typeof(int), typeof(bool) });
            _previewRefreshPrefix = AccessTools.Method(
                typeof(PreviewEmptyTalkEffectPatch),
                nameof(PreviewEmptyTalkEffectPatch.Prefix));

            if (_normalFactoryTarget == null || _normalFactoryPrefix == null)
            {
                throw new MissingMethodException(
                    "未找到正常游戏 CommonEvtMgr.GenEffector(List<float>, Effector, int, int) 或其 1163 Prefix。");
            }

            if (_runtimeTextStartTarget == null || _runtimeTextStartPrefix == null ||
                _previewTextStartTarget == null || _previewTextStartPrefix == null ||
                _previewRefreshTarget == null || _previewRefreshPrefix == null)
            {
                throw new MissingMethodException(
                    "未找到正常游戏或编辑器 Preview 的 1163 文字开始入口及其 Prefix。");
            }
        }

        private bool AreCriticalPatchesInstalled()
        {
            return HasPrefix(_normalFactoryTarget, _normalFactoryPrefix) &&
                   HasPrefix(_runtimeTextStartTarget, _runtimeTextStartPrefix) &&
                   HasPrefix(_previewTextStartTarget, _previewTextStartPrefix) &&
                   HasPrefix(_previewRefreshTarget, _previewRefreshPrefix);
        }

        private static bool HasPrefix(MethodInfo target, MethodInfo prefix)
        {
            if (target == null || prefix == null)
            {
                return false;
            }

            HarmonyLib.Patches patchInfo = Harmony.GetPatchInfo(target);
            return patchInfo != null && patchInfo.Prefixes.Any(
                patch => patch.owner == PluginGuid && patch.PatchMethod == prefix);
        }

        private void EnsureCriticalPatchesInstalled()
        {
            if (_isRepairingCriticalPatches)
            {
                return;
            }

            _isRepairingCriticalPatches = true;
            try
            {
                if (_normalFactoryTarget == null || _normalFactoryPrefix == null ||
                    _runtimeTextStartTarget == null || _runtimeTextStartPrefix == null ||
                    _previewTextStartTarget == null || _previewTextStartPrefix == null ||
                    _previewRefreshTarget == null || _previewRefreshPrefix == null)
                {
                    ResolveCriticalPatchMethods();
                }

                InstallPrefixIfMissing(
                    _normalFactoryTarget,
                    _normalFactoryPrefix,
                    new[] { "lince.multiplelovers" });
                InstallPrefixIfMissing(
                    _runtimeTextStartTarget,
                    _runtimeTextStartPrefix,
                    null);
                InstallPrefixIfMissing(
                    _previewTextStartTarget,
                    _previewTextStartPrefix,
                    null);
                InstallPrefixIfMissing(
                    _previewRefreshTarget,
                    _previewRefreshPrefix,
                    null);

                if (!AreCriticalPatchesInstalled())
                {
                    throw new InvalidOperationException(
                        "Harmony.Patch 返回后，正常游戏或编辑器的关键 EFFECT 入口仍不完整。");
                }
            }
            finally
            {
                _isRepairingCriticalPatches = false;
            }
        }

        private bool InstallPrefixIfMissing(
            MethodInfo target,
            MethodInfo prefix,
            string[] beforeOwners)
        {
            if (HasPrefix(target, prefix))
            {
                return false;
            }

            var harmonyMethod = new HarmonyMethod(prefix)
            {
                priority = Priority.First,
                before = beforeOwners
            };
            _harmony.Patch(target, prefix: harmonyMethod);
            return true;
        }

        private void VerifyCriticalEffectPaths()
        {
            // 正常游戏 NewTalkView 会通过批次重载，批次重载再逐条调用单条重载；
            // 两条路径都验证，防止只修好直接调用却仍在实际 Talk 中 Effect Not Found。
            var directProbe = new List<float> { 1163f, 0f };
            Effector directResult = CommonEvtMgr.GenEffector(directProbe, null, 0, 0);
            if (!(directResult is EffectorBetterAudio))
            {
                throw new InvalidOperationException(
                    "正常游戏单条 GenEffector 自检失败，1163 未返回 EffectorBetterAudio。");
            }

            var batchProbe = new List<List<float>>
            {
                new List<float> { 1163f, 0f }
            };
            Effector batchResult = CommonEvtMgr.GenEffector(batchProbe, null, 0, 0);
            if (!(batchResult is EffectorBetterAudio))
            {
                throw new InvalidOperationException(
                    "正常游戏批次 GenEffector 自检失败，Talk EFFECT 链仍可能出现 Effect Not Found。");
            }

            if (!HasPrefix(_runtimeTextStartTarget, _runtimeTextStartPrefix) ||
                !HasPrefix(_previewTextStartTarget, _previewTextStartPrefix) ||
                !HasPrefix(_previewRefreshTarget, _previewRefreshPrefix))
            {
                throw new InvalidOperationException(
                    "正常游戏或编辑器 Preview 的 1163 提前入口自检失败。");
            }

        }

        // 关键：BetterAudioController 是 DontDestroyOnLoad 独立对象。
        // 即使 BaseUnityPlugin 在游戏启动阶段被销毁，它仍会每秒检查并补回关键 EFFECT Patch。
        internal void TickPatchHealthFromPersistentController()
        {
            if (_applicationQuitting || _harmony == null)
            {
                return;
            }

            if (Time.unscaledTime < _nextPatchHealthCheckTime)
            {
                return;
            }

            _nextPatchHealthCheckTime = Time.unscaledTime + PatchHealthCheckInterval;

            try
            {
                if (!AreCriticalPatchesInstalled())
                {
                    EnsureCriticalPatchesInstalled();
                    VerifyCriticalEffectPaths();
                }
            }
            catch
            {
                // 运行期保活静默重试；仅启动自检与实际 1163 执行结果写入控制台。
            }
        }

        internal void NotifyApplicationQuitFromPersistentController()
        {
            if (_applicationQuitting)
            {
                return;
            }

            _applicationQuitting = true;
            try
            {
                _harmony?.UnpatchSelf();
            }
            catch
            {
                // 退出阶段仅做 best-effort 清理。
            }
        }

        private void OnApplicationQuit()
        {
            _applicationQuitting = true;
            BetterAudioController.Instance?.Shutdown();
            _harmony?.UnpatchSelf();
        }

        private void OnDestroy()
        {
            try
            {
                if (!_applicationQuitting)
                {
                    // 游戏启动阶段可能销毁 BepInEx 所挂载的 BaseUnityPlugin 组件。
                    // 这里绝不能 Shutdown 或 UnpatchSelf，否则启动自检之后 1163 会立即失效。
                    return;
                }

                BetterAudioController.Instance?.Shutdown();
                _harmony?.UnpatchSelf();

                if (ReferenceEquals(Instance, this))
                {
                    Instance = null;
                }
            }
            catch
            {
                // Unity 退出或销毁 native object 时静默完成 best-effort 清理。
            }
        }
    }
}
