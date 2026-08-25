using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EC2BUnofficialPatch.Core;
using EC2BUnofficialPatch.Services;
using HarmonyLib;
using UnityEngine.InputSystem;

namespace EC2BUnofficialPatch.Features.Optimization
{
    internal sealed class JsonHotReloadModule : IPluginModule
    {
        private static readonly IReadOnlyList<ModuleLogItem> Items = new[]
        {
            new ModuleLogItem("优化", ".json文件热重载")
        };

        public string Key => "optimization.json-hot-reload";
        public IReadOnlyList<ModuleLogItem> LogItems => Items;

        public void Load(Harmony harmony, PluginServices services)
        {
            MethodInfo merge = AccessTools.Method(typeof(ModCtrl), "MergeCfgsAsync")
                ?? throw new MissingMethodException(typeof(ModCtrl).FullName, "MergeCfgsAsync");
            harmony.Patch(
                merge,
                prefix: new HarmonyMethod(
                    typeof(GameModCfgHotReload),
                    nameof(GameModCfgHotReload.CaptureBaseBeforeModMerge)));

            JsonHotReloadRuntime.Initialize();
            PatchLog.Registration(
                "优化模块-.json文件热重载初始化完成：快捷键=F9，" +
                "范围=普通Mod CFG、UP扩展JSON、BetterAudio及公开接口提供者");
        }
    }

    internal static class JsonHotReloadRuntime
    {
        private static bool _initialized;
        private static bool _reloading;
        private static bool _inputBackendFailed;

        internal static void Initialize()
        {
            JsonHotReloadApi.EnsureBuiltIn(
                "up.game-mod-cfg",
                "普通 Mod CFG",
                GameModCfgHotReload.Reload,
                100);
            JsonHotReloadApi.EnsureBuiltIn(
                "up.extension-json",
                "UP 扩展 JSON",
                PluginRuntime.ReloadExtensionJson,
                200);
            JsonHotReloadApi.EnsureBuiltIn(
                "compat.betteraudio",
                "BetterAudio.json",
                BetterAudioJsonHotReload.Reload,
                300);
            _initialized = true;
        }

        internal static void Tick()
        {
            if (!_initialized ||
                PluginConfig.JsonHotReload?.Value != true ||
                _reloading)
            {
                return;
            }

            BetterAudioJsonHotReload.EnsureBaseline();
            if (!IsReloadPressed())
                return;

            ReloadAll();
        }

        private static bool IsReloadPressed()
        {
            if (_inputBackendFailed)
                return false;

            try
            {
                Keyboard keyboard = Keyboard.current;
                return keyboard != null && keyboard.f9Key.wasPressedThisFrame;
            }
            catch (Exception exception)
            {
                _inputBackendFailed = true;
                PatchLog.Error(
                    "优化模块-.json热重载无法读取新输入系统，已停止快捷键轮询以避免重复报错：" +
                    ModuleHost.GetReason(exception));
                return false;
            }
        }

        private static void ReloadAll()
        {
            _reloading = true;
            int succeeded = 0;
            int skipped = 0;
            int failed = 0;
            int files = 0;
            int entries = 0;

            try
            {
                IReadOnlyList<JsonHotReloadApi.ProviderRegistration> providers =
                    JsonHotReloadApi.Snapshot();
                PatchLog.Info(
                    $"优化模块-收到 F9 .json 热重载请求：providers={providers.Count}");

                foreach (JsonHotReloadApi.ProviderRegistration provider in providers)
                {
                    JsonHotReloadResult result;
                    try
                    {
                        result = provider.Reload() ??
                                 JsonHotReloadResult.Failed("提供者返回了空结果");
                    }
                    catch (Exception exception)
                    {
                        result = JsonHotReloadResult.Failed(ModuleHost.GetReason(exception));
                    }

                    if (!result.Success)
                    {
                        failed++;
                        PatchLog.Error(
                            $"优化模块-.json热重载失败：provider={provider.DisplayName}, " +
                            $"id={provider.Id}, reason={result.Message}");
                        continue;
                    }

                    if (result.Skipped)
                    {
                        skipped++;
                        continue;
                    }

                    succeeded++;
                    files += result.FileCount;
                    entries += result.EntryCount;
                    PatchLog.Info(
                        $"优化模块-.json热重载完成：provider={provider.DisplayName}, " +
                        $"files={result.FileCount}, entries={result.EntryCount}, detail={result.Message}");
                }

                PatchLog.Info(
                    "优化模块-F9 .json文件热重载结束：" +
                    $"成功提供者={succeeded}, 跳过={skipped}, 失败={failed}, " +
                    $"文件={files}, 条目={entries}");
            }
            finally
            {
                _reloading = false;
            }
        }
    }
}
