using System;
using System.Collections.Generic;
using System.Reflection;
using EC2BUnofficialPatch.Core;
using Effect;
using HarmonyLib;

namespace EC2BUnofficialPatch.Features.ScreenEffects
{
    internal sealed class ScreenVideoModule : IPluginModule
    {
        internal const int EffectId = 1164;

        private static ScreenVideoRegistry _registry;
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

            PatchLog.Registration($"屏幕特效模块-1164屏幕视频注册完成：视频={_registry.Count}");
        }

        internal static void ReplaceRegistry(ScreenVideoRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
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

        internal static void Execute(ScreenVideoRequest request)
        {
            if (request.Stop)
            {
                ScreenVideoPlayer.Stop("effect-stop");
                return;
            }

            if (_registry == null || !_registry.TryGet(request.VideoId, out RegisteredScreenVideo video))
            {
                PatchLog.Warning($"1164屏幕视频扩展-未注册的视频 id={request.VideoId}，已忽略");
                return;
            }
            if (!System.IO.File.Exists(video.VideoPath))
            {
                _registry.ReportMissingFile(video);
                return;
            }
            ScreenVideoPlayer.Play(video);
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
