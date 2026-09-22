using System;
using System.Collections.Generic;
using System.Reflection;
using Config;
using EC2BUnofficialPatch.Core;
using EC2BUnofficialPatch.Workshop;
using Effect;
using HarmonyLib;
using LFBetterAudio.Preview;
using Sdk;
using View.Mod;
using View.Evt;

namespace EC2BUnofficialPatch.Features.ScreenEffects
{
    internal sealed class ScreenVideoModule : IPluginModule
    {
        internal const int EffectId = 1164;

        private static ScreenVideoRegistry _registry;
        private static ScreenVideoRegistry _previewRegistry;
        private static readonly FieldInfo PreviewRootField =
            AccessTools.Field(typeof(ModPreviewTipsView), "root");
        private static readonly MethodInfo PrefixMethod =
            AccessTools.Method(typeof(ScreenVideoModule), nameof(GenEffectorPrefix));
        private static readonly IReadOnlyList<ModuleLogItem> Items = new[]
        {
            new ModuleLogItem("屏幕特效", "1164-屏幕视频扩展")
        };

        public string Key => "screen.1164";

        public IReadOnlyList<ModuleLogItem> LogItems => Items;

        public void Load(Harmony harmony, PluginServices services)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            _registry = ScreenVideoRegistry.Load(services.ContentRoots.Roots);

            MethodInfo target = AccessTools.Method(
                typeof(CommonEvtMgr),
                nameof(CommonEvtMgr.GenEffector),
                new[] { typeof(List<float>), typeof(Effector), typeof(int), typeof(int) })
                ?? throw new MissingMethodException(typeof(CommonEvtMgr).FullName, "GenEffector");
            harmony.Patch(target, prefix: new HarmonyMethod(PrefixMethod));
            foreach (string hotKeyMethod in new[] { "OnHotKeyInput", "OnHotKeyStartPress", "OnHotKeyPressing" })
            {
                MethodInfo method = AccessTools.Method(typeof(NewTalkView), hotKeyMethod, new[] { typeof(int) });
                if (method != null)
                    harmony.Patch(method, prefix: new HarmonyMethod(
                        AccessTools.Method(typeof(ScreenVideoModule), nameof(BlockTalkHotKey))));
            }
            harmony.Patch(
                AccessTools.Method(typeof(PreviewTalkView), nameof(PreviewTalkView.DoText)),
                prefix: new HarmonyMethod(typeof(ScreenVideoModule), nameof(PreviewDoTextPrefix)));
            harmony.Patch(
                AccessTools.Method(typeof(PreviewTalkView), nameof(PreviewTalkView.RefreshTalk)),
                prefix: new HarmonyMethod(typeof(ScreenVideoModule), nameof(PreviewRefreshTalkPrefix)));
            harmony.Patch(
                AccessTools.Method(typeof(PreviewTalkView), nameof(PreviewTalkView.OnClose)),
                prefix: new HarmonyMethod(typeof(ScreenVideoModule), nameof(PreviewOnClosePrefix)));
            harmony.Patch(
                AccessTools.Method(typeof(PreviewTalkView), nameof(PreviewTalkView.OnHotKeyInput)),
                prefix: new HarmonyMethod(typeof(ScreenVideoModule), nameof(BlockTalkHotKey)));
            foreach (string hotKeyMethod in new[] { "OnHotKeyStartPress", "OnHotKeyPressing" })
            {
                harmony.Patch(
                    AccessTools.Method(typeof(BaseView), hotKeyMethod, new[] { typeof(int) }),
                    prefix: new HarmonyMethod(typeof(ScreenVideoModule), nameof(BlockPreviewHotKey)));
            }
            harmony.Patch(
                AccessTools.Method(typeof(ModPreviewTipsView), "OnClickOK"),
                prefix: new HarmonyMethod(typeof(ScreenVideoModule), nameof(PreviewOpenPrefix)));

            PatchLog.Registration($"屏幕视频模块-1164屏幕视频注册完成：视频={_registry.Count}");
        }

        internal static void ReplaceRegistry(ScreenVideoRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        private static bool BlockTalkHotKey(ref bool __result)
        {
            if (!ScreenVideoPlayer.BlocksInput) return true;
            __result = true;
            return false;
        }

        private static bool BlockPreviewHotKey(BaseView __instance, ref bool __result)
        {
            return !(__instance is PreviewTalkView) || BlockTalkHotKey(ref __result);
        }

        private static void PreviewOpenPrefix(ModPreviewTipsView __instance)
        {
            _previewRegistry = null;
            try
            {
                string root = PreviewRootField?.GetValue(__instance) as string;
                if (string.IsNullOrWhiteSpace(root)) return;
                _previewRegistry = ScreenVideoRegistry.Load(new[] { new ContentRoot("editor-preview", root) });
            }
            catch (Exception exception)
            {
                PatchLog.Warning("1164屏幕视频扩展-编辑器预览资源加载失败：" + ModuleHost.GetReason(exception));
            }
        }

        private static void PreviewRefreshTalkPrefix(PreviewTalkView __instance, int __0)
        {
            Dictionary<int, TalkCfg> map = PreviewTalkAccess.GetTalkCfgMap(__instance);
            if (map == null || !map.TryGetValue(__0, out TalkCfg cfg) || cfg == null ||
                !string.IsNullOrWhiteSpace(cfg.content) ||
                cfg.id == PreviewTalkAccess.GetLastEffectCfgId(__instance)) return;
            ExecutePreviewEffects(__instance, cfg);
        }

        private static void PreviewDoTextPrefix(PreviewTalkView __instance)
        {
            TalkCfg cfg = PreviewTalkAccess.GetCurrentCfg(__instance);
            if (cfg == null || cfg.id == PreviewTalkAccess.GetLastEffectCfgId(__instance)) return;
            ExecutePreviewEffects(__instance, cfg);
        }

        private static void ExecutePreviewEffects(PreviewTalkView view, TalkCfg cfg)
        {
            if (cfg.effect == null) return;
            foreach (List<float> effect in cfg.effect)
            {
                if (effect == null || effect.Count == 0 || (int)effect[0] != EffectId) continue;
                if (!TryParse(effect, out ScreenVideoRequest request, out string error))
                {
                    PatchLog.Warning($"1164屏幕视频扩展-编辑器预览指令无效：{error}");
                    continue;
                }
                try { Execute(request, view); }
                catch (Exception exception)
                {
                    PatchLog.Warning("1164屏幕视频扩展-编辑器预览执行失败：" + ModuleHost.GetReason(exception));
                }
            }
        }

        private static void PreviewOnClosePrefix(PreviewTalkView __instance)
        {
            ScreenVideoPlayer.StopPreview(__instance);
            _previewRegistry = null;
        }

        private static bool GenEffectorPrefix(List<float> __0, Effector __1, int __2, int __3, ref Effector __result)
        {
            if (__0 == null || __0.Count == 0 || (int)__0[0] != EffectId) return true;

            ScreenVideoRequest request;
            if (!TryParse(__0, out request, out string error))
            {
                PatchLog.Warning($"1164屏幕视频扩展-指令无效，已忽略：{error}, effect=[{string.Join(",", __0)}]");
                __result = __1;
                return false;
            }

            __result = new EffectorScreenVideo(__1, __0, request) { toRoleId = __2, fromRoleId = __3 };
            return false;
        }

        internal static bool TryParse(List<float> effect, out ScreenVideoRequest request, out string error)
        {
            request = default;
            error = null;
            if (effect.Count < 2) { error = "缺少子命令（0=停止，1=播放）"; return false; }
            int command = (int)effect[1];
            switch (command)
            {
                case 0:
                    request = new ScreenVideoRequest { Stop = true };
                    return true;
                case 1:
                    if (effect.Count < 3 || effect[2] <= 0) { error = "1164,1 需要视频 id"; return false; }
                    request = new ScreenVideoRequest { VideoId = (int)effect[2] };
                    return true;
                default:
                    error = "未知子命令 " + command;
                    return false;
            }
        }

        internal static void Execute(ScreenVideoRequest request, PreviewTalkView previewOwner = null)
        {
            if (request.Stop)
            {
                ScreenVideoPlayer.Stop("effect-stop");
                return;
            }

            ScreenVideoRegistry registry = previewOwner != null ? _previewRegistry ?? _registry : _registry;
            if (registry == null || !registry.TryGet(request.VideoId, out RegisteredScreenVideo video))
            {
                PatchLog.Warning($"1164屏幕视频扩展-未注册的视频 id={request.VideoId}，已忽略");
                return;
            }
            if (!System.IO.File.Exists(video.VideoPath))
            {
                registry.ReportMissingFile(video);
                return;
            }
            ScreenVideoPlayer.Play(video, previewOwner);
        }
    }

    internal struct ScreenVideoRequest
    {
        internal bool Stop;
        internal int VideoId;
    }

    public sealed class EffectorScreenVideo : Effector
    {
        private readonly ScreenVideoRequest _request;

        internal EffectorScreenVideo(Effector previous, List<float> effect, ScreenVideoRequest request)
            : base(previous, effect)
        {
            _request = request;
        }

        public override void OnRun(float _rate = 1f, bool _toast = false)
        {
            try
            {
                ScreenVideoModule.Execute(_request);
            }
            catch (Exception exception)
            {
                PatchLog.Warning("1164屏幕视频扩展-执行失败：" + ModuleHost.GetReason(exception));
            }
        }

        public override string OnToString(float _rate = 1f, int _type = 0)
        {
            return null;
        }
    }
}
