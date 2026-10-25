using System;
using System.Collections.Generic;
using System.Reflection;
using EC2BUnofficialPatch.Core;
using HarmonyLib;
using Sdk;

namespace EC2BUnofficialPatch.Features.Optimization
{
    internal sealed class AutoOpenGameConsoleModule : IPluginModule
    {
        private static readonly IReadOnlyList<ModuleLogItem> Items = new[]
        {
            new ModuleLogItem("优化", "启动后自动打开游戏内控制台")
        };

        public string Key => "optimization.auto-open-game-console";
        public IReadOnlyList<ModuleLogItem> LogItems => Items;

        public void Load(Harmony harmony, PluginServices services)
        {
            AutoOpenGameConsolePatches.Initialize();
            MethodInfo start = AccessTools.Method(typeof(DebugView), "Start")
                ?? throw new MissingMethodException(typeof(DebugView).FullName, "Start");
            harmony.Patch(
                start,
                postfix: new HarmonyMethod(
                    typeof(AutoOpenGameConsolePatches),
                    nameof(AutoOpenGameConsolePatches.StartPostfix)));
            harmony.Patch(
                AccessTools.Method(typeof(UIMgr), nameof(UIMgr.Init))
                    ?? throw new MissingMethodException(typeof(UIMgr).FullName, "Init"),
                postfix: new HarmonyMethod(typeof(AutoOpenGameConsolePatches),
                    nameof(AutoOpenGameConsolePatches.UIReadyPostfix)));
            harmony.Patch(
                AccessTools.Method(typeof(Control), nameof(Control.OnSubmitHide))
                    ?? throw new MissingMethodException(typeof(Control).FullName, "OnSubmitHide"),
                prefix: new HarmonyMethod(typeof(AutoOpenGameConsolePatches),
                    nameof(AutoOpenGameConsolePatches.SubmitHidePrefix)));

            PatchLog.Registration("优化模块-启动后自动打开游戏内控制台已启用");
        }
    }

    internal static class AutoOpenGameConsolePatches
    {
        private static bool _uiReady;
        private static DebugView _pendingView;

        internal static void Initialize()
        {
            object manager = AccessTools.Field(typeof(UIMgr), "ins").GetValue(null);
            _uiReady = manager != null && AccessTools.Field(typeof(UIMgr), "viewDict").GetValue(manager) != null;
            _pendingView = null;
        }

        public static void StartPostfix(DebugView __instance)
        {
            _pendingView = __instance;
            TryOpen();
        }

        public static void UIReadyPostfix()
        {
            _uiReady = true;
            TryOpen();
        }

        // Control enables UI input before UIMgr.Init creates viewDict. Opening or
        // closing the debug console can also dispatch a canceled SubmitHide callback.
        public static bool SubmitHidePrefix() => _uiReady;

        private static void TryOpen()
        {
            if (!_uiReady || _pendingView == null) return;
            DebugView view = _pendingView;
            _pendingView = null;
            DebugMgr manager = DebugMgr.Ins;
            if (manager == null)
            {
                PatchLog.Warning("优化模块-游戏内控制台初始化时缺少 DebugView 或 DebugMgr，保持原版关闭状态");
                return;
            }

            bool previousEnableDebug = manager.enableDebug;
            try
            {
                manager.enableDebug = true;
                view.SetConsoleShow(true);
                PatchLog.Info("优化模块-游戏内控制台已自动打开；可使用 ~ 切换、Esc 关闭");
            }
            catch (Exception exception)
            {
                manager.enableDebug = previousEnableDebug;
                try
                {
                    view.SetConsoleShow(false);
                }
                catch
                {
                    // 回退失败时保留原始异常作为唯一诊断来源。
                }
                PatchLog.Warning(
                    "优化模块-游戏内控制台自动打开失败，已回退原版状态：" +
                    ModuleHost.GetReason(exception));
            }
        }
    }
}
