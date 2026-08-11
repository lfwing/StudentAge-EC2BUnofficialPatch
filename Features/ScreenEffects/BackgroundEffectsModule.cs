using System;
using System.Collections.Generic;
using System.Reflection;
using Coffee.UIEffects;
using EC2BUnofficialPatch.Core;
using HarmonyLib;
using UnityEngine;
using View.Evt;

namespace EC2BUnofficialPatch.Features.ScreenEffects
{
    internal sealed class BackgroundEffectsModule : IPluginModule
    {
        private static readonly MethodInfo NewPrefixMethod =
            AccessTools.Method(typeof(BackgroundEffectsModule), nameof(NewTalkPrefix));
        private static readonly MethodInfo PreviewPrefixMethod =
            AccessTools.Method(typeof(BackgroundEffectsModule), nameof(PreviewTalkPrefix));

        private static readonly IReadOnlyList<ModuleLogItem> Items = new[]
        {
            new ModuleLogItem("屏幕特效", "4021-屏幕黑白特效新增"),
            new ModuleLogItem("屏幕特效", "4022-屏幕打码特效新增")
        };

        public string Key => "screen.4021-4022";

        public IReadOnlyList<ModuleLogItem> LogItems => Items;

        public void Load(Harmony harmony, PluginServices services)
        {
            TalkViewAccessor.Validate(typeof(NewTalkView));
            TalkViewAccessor.Validate(typeof(PreviewTalkView));

            harmony.Patch(
                RequireMethod(typeof(NewTalkView), "PlayBgEffect"),
                prefix: new HarmonyMethod(NewPrefixMethod));
            harmony.Patch(
                RequireMethod(typeof(PreviewTalkView), "PlayBgEffect"),
                prefix: new HarmonyMethod(PreviewPrefixMethod));
        }

        private static bool NewTalkPrefix(NewTalkView __instance, ref bool __result)
        {
            if (!TryApply(__instance))
            {
                return true;
            }

            __result = true;
            return false;
        }

        private static bool PreviewTalkPrefix(PreviewTalkView __instance)
        {
            return !TryApply(__instance);
        }

        private static bool TryApply(object talkView)
        {
            List<float> screenEffect = TalkViewAccessor.GetScreenEffect(talkView);
            if (screenEffect == null || screenEffect.Count == 0)
            {
                return false;
            }

            int command = (int)screenEffect[0];
            if (command != CommandIds.Grayscale && command != CommandIds.Pixel)
            {
                return false;
            }

            try
            {
                float factor = screenEffect.Count > 1
                    ? Mathf.Clamp01(screenEffect[1])
                    : 1f;
                GameObject background = TalkViewAccessor.GetCurrentBackground(talkView);
                UIEffect effect = background?.GetComponent<UIEffect>();
                if (effect != null)
                {
                    effect.effectMode =
                        command == CommandIds.Grayscale
                            ? EffectMode.Grayscale
                            : EffectMode.Pixel;
                    effect.effectFactor = factor;
                }
                PatchLog.Info($"屏幕特效模块-{command}屏幕效果调用：factor={factor:0.###}");
            }
            catch (Exception exception)
            {
                PatchLog.Exception($"屏幕特效模块-{command}屏幕效果应用失败", exception);
            }

            return true;
        }

        private static MethodInfo RequireMethod(Type type, string name)
        {
            return AccessTools.Method(type, name)
                ?? throw new MissingMethodException(type.FullName, name);
        }
    }
}
