using System;
using BepInEx.Logging;
using EC2BUnofficialPatch.Features.ActionCommands;
using EC2BUnofficialPatch.Features.Effects;
using EC2BUnofficialPatch.Features.Mechanics;
using EC2BUnofficialPatch.Features.Mechanics.LoveDraw;
using EC2BUnofficialPatch.Features.Mechanics.AudioTrace;
using EC2BUnofficialPatch.Features.Optimization.StaticPortraitOptimization;
using EC2BUnofficialPatch.Features.Optimization.CGOptimization;
using EC2BUnofficialPatch.Features.Optimization;
using EC2BUnofficialPatch.Features.ScreenEffects;
using EC2BUnofficialPatch.Features.ScreenEffects.ScreenLyrcis;
using UnityEngine;

namespace EC2BUnofficialPatch.Core
{
    /// <summary>
    /// 插件真正的生命周期所有者。BaseUnityPlugin 仅负责首次引导。
    /// </summary>
    internal static class PluginRuntime
    {
        private static readonly object SyncRoot = new object();

        private static ManualLogSource _logger;
        private static PluginServices _services;
        private static ModuleHost _moduleHost;
        private static PluginRuntimeHost _runtimeHost;
        private static bool _initialized;
        private static bool _applicationQuitting;
        private static bool _shutdownCompleted;

        internal static void Start(
            ManualLogSource logger,
            string bootstrapHost,
            int bootstrapComponentId)
        {
            lock (SyncRoot)
            {
                _logger = logger ?? _logger;
                PatchLog.Initialize(_logger);

                if (_initialized && !_shutdownCompleted)
                {
                    PatchLog.Warning(
                        "底层服务模块-检测到重复引导组件，沿用现有持久运行时：" +
                        $"bootstrapHost={bootstrapHost}, componentId={bootstrapComponentId}, " +
                        $"runtimeHost={DescribeRuntimeHost()}");
                    return;
                }

                _applicationQuitting = false;
                _shutdownCompleted = false;

                try
                {
                    _runtimeHost = PluginRuntimeHost.Create();
                    if (_runtimeHost == null)
                    {
                        throw new InvalidOperationException("无法创建 EC2BUnofficialPatch_RuntimeHost。");
                    }

                    InitializeModules();
                    _initialized = true;

                    PatchLog.Info(
                        "EC2BUnofficialPatch 启动完成：" +
                        $"version=1.0.16.2, runtimeHost={DescribeRuntimeHost()}");
                }
                catch (Exception exception)
                {
                    PatchLog.Exception("底层服务模块-统一启动加载失败", exception);
                    ShutdownInternal("启动失败清理", true);
                }
            }
        }

        internal static void NotifyBootstrapDestroyed(
            string bootstrapHost,
            int bootstrapComponentId)
        {
            lock (SyncRoot)
            {
                if (_applicationQuitting)
                {
                    return;
                }

                PatchLog.Warning(
                    "EC2BUnofficialPatch 引导组件被游戏销毁；持久运行时继续工作，" +
                    "不撤销 Harmony 补丁、不释放资源：" +
                    $"host={bootstrapHost}, componentId={bootstrapComponentId}, " +
                    $"runtimeHost={DescribeRuntimeHost()}");
            }
        }

        internal static void BeginApplicationQuit(string source)
        {
            lock (SyncRoot)
            {
                if (_applicationQuitting)
                {
                    return;
                }

                _applicationQuitting = true;
                PatchLog.Debug($"底层服务模块-收到应用退出信号：source={source}");
                ShutdownInternal("应用退出", false);
            }
        }

        internal static void NotifyRuntimeHostDestroyed(PluginRuntimeHost destroyedHost)
        {
            lock (SyncRoot)
            {
                if (ReferenceEquals(_runtimeHost, destroyedHost))
                {
                    _runtimeHost = null;
                }

                if (!_applicationQuitting && !_shutdownCompleted)
                {
                    PatchLog.Error(
                        "底层服务模块-持久运行时宿主被意外销毁；" +
                        "现有 Harmony 补丁不会因此自动撤销，但退出清理回调将不可用。");
                }
            }
        }

        private static void InitializeModules()
        {
            TemplateMaintenance.Ensure();

            _services = PluginServices.Create();

            _moduleHost = new ModuleHost(_logger, _services);

            // 必须在游戏开始读取 Workshop CFG 之前挂接。该补丁为每个 Mod 构建运行时 CFG 视图：
            // 有增强版时替代同名兼容 CFG；原版同名 CFG 不存在时也能主动注入。Workshop 原文件始终不修改。
            if (PluginConfig.LoveDrawExternalResources.Value || PluginConfig.MinigameMechanics.Value)
            {
                _moduleHost.Load(new ModCfgOverrideModule());
            }

            LoadIf(PluginConfig.ScreenDynamicWaitText.Value, new DynamicWaitTextModule(), "屏幕特效/4006黑屏特效显示文字扩展");
            LoadIf(PluginConfig.ScreenComicExtension.Value, new ComicExtensionModule(), "屏幕特效/4016漫画显示扩展");
            LoadIf(PluginConfig.ScreenBackgroundEffects.Value, new BackgroundEffectsModule(), "屏幕特效/屏幕特效扩展");
            LoadIf(PluginConfig.ScreenPaper.Value, new ScreenPaperModule(), "屏幕特效/5001屏幕纸条扩展");
            LoadIf(PluginConfig.ScreenLyrics.Value, new ScreenLyrcisModule(), "屏幕特效/5002屏幕滚动歌词扩展");
            LoadIf(PluginConfig.Action3003.Value, new Action3003Module(), "行动指令/3003修复");
            LoadIf(PluginConfig.AnimeExtension.Value, new AnimeExtensionModule(), "效果/36动画相关修复与扩展");
            LoadIf(PluginConfig.MapMoveEffects.Value, new MapMoveEffectModule(), "效果/100,1地点移动修复");
            LoadIf(PluginConfig.RelationEffects.Value, new RelationEffectModule(), "效果/20关系效果修复与扩展");
            LoadIf(PluginConfig.LoveDrawExternalResources.Value, new LoveDrawExternalResourceModule(), "机制/情侣画修复");
            LoadIf(PluginConfig.MinigameMechanics.Value, new MechanicsModule(), "机制/社交小游戏修复");
            LoadIf(PluginConfig.RoleAvailability.Value, new RoleAvailabilityModule(), "机制/控制角色在列表显示");
            bool anyAudioTrace = PluginConfig.AudioOriginalChannel.Value ||
                                 PluginConfig.AudioBetterAudioChannel.Value ||
                                 PluginConfig.AudioUnityChannel.Value;
            if (anyAudioTrace)
            {
                _moduleHost.Load(new AudioPlaybackTraceModule(
                    PluginConfig.AudioOriginalChannel.Value,
                    PluginConfig.AudioBetterAudioChannel.Value,
                    PluginConfig.AudioUnityChannel.Value));
            }
            LoadIf(PluginConfig.StaticPortraitOptimization.Value, new StaticPortraitOptimizationModule(), "优化/静态立绘优化");
            LoadIf(PluginConfig.CGOptimization.Value, new CGOptimizationModule(), "优化/CG播放与图鉴排序优化");
            LoadIf(PluginConfig.ExamManualScore.Value, new ExamManualScoreModule(), "优化/普通考试允许手动输入成绩");
            _moduleHost.Load(new global::EC2BUnofficialPatch.Features.Optimization.LoveTopicLimitModule());
            LoadIf(
                PluginConfig.RelationFocusCount.Value,
                new global::EC2BUnofficialPatch.Features.Optimization.RelationFocusCountModule(),
                "优化/关注人数统计优化");

            _moduleHost.LogSelfCheckSummary();
            PatchLog.FlushRegistrations();
            int loveTopicLimit =
                global::EC2BUnofficialPatch.Features.Optimization.LoveTopicPatches.GetSocialTopicLimit();
            PatchLog.Info($"优化模块-情侣话题每回合次数：value={loveTopicLimit}");
        }


        private static void LoadIf(bool enabled, IPluginModule module, string configName)
        {
            if (enabled)
            {
                _moduleHost.Load(module);
                return;
            }

            // 关闭的功能不属于自检失败，不额外污染启动日志。
        }

        private static void ShutdownInternal(string reason, bool destroyRuntimeHost)
        {
            if (_shutdownCompleted)
            {
                return;
            }

            _shutdownCompleted = true;
            PatchLog.Debug($"底层服务模块-开始退出清理：reason={reason}");

            try
            {
                _moduleHost?.Dispose();
            }
            catch (Exception exception)
            {
                PatchLog.Exception("底层服务模块-Harmony 卸载失败", exception);
            }

            try
            {
                _services?.Dispose();
            }
            catch (Exception exception)
            {
                PatchLog.Exception("底层服务模块-资源服务释放失败", exception);
            }

            _moduleHost = null;
            _services = null;
            _initialized = false;

            if (destroyRuntimeHost && _runtimeHost != null)
            {
                PluginRuntimeHost host = _runtimeHost;
                _runtimeHost = null;
                UnityEngine.Object.Destroy(host.gameObject);
            }

            PatchLog.Debug("EC2BUnofficialPatch 已完成应用退出清理");
        }

        private static string DescribeRuntimeHost()
        {
            return _runtimeHost == null
                ? "<null>"
                : $"{_runtimeHost.gameObject.name}[componentId={_runtimeHost.GetInstanceID()}," +
                  $"objectId={_runtimeHost.gameObject.GetInstanceID()}]";
        }
    }
}
