using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using EC2BUnofficialPatch.Core;
using HarmonyLib;
using Config;
using UnityEngine;
using UnityEngine.UI;
using View.Evt;

namespace EC2BUnofficialPatch.Features.ScreenEffects
{
    internal sealed class ScreenPaperModule : IPluginModule
    {
        private static readonly ConditionalWeakTable<PaperView, PaperVisualState> States =
            new ConditionalWeakTable<PaperView, PaperVisualState>();

        private static PluginServices _services;
        private static ScreenPaperRegistry _registry;

        private static readonly MethodInfo PostfixMethod =
            AccessTools.Method(typeof(ScreenPaperModule), nameof(OnOpenPostfix));
        private static readonly IReadOnlyList<ModuleLogItem> Items = new[]
        {
            new ModuleLogItem("屏幕特效", "5001-屏幕纸条扩展")
        };

        public string Key => "screen.5001";

        public IReadOnlyList<ModuleLogItem> LogItems => Items;

        public void Load(Harmony harmony, PluginServices services)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _registry = ScreenPaperRegistry.Load(_services.ContentRoots.Roots);
            MethodInfo target = AccessTools.Method(typeof(PaperView), "OnOpen")
                ?? throw new MissingMethodException(typeof(PaperView).FullName, "OnOpen");
            harmony.Patch(target, postfix: new HarmonyMethod(PostfixMethod));

            PatchLog.Registration($"屏幕特效模块-5001屏幕纸条注册完成：图片覆盖={_registry.Count}");
        }

        private static void OnOpenPostfix(PaperView __instance)
        {
            try
            {
                Image background = __instance.group_paper?.GetComponent<Image>();
                if (background == null)
                {
                    return;
                }

                PaperVisualState state = States.GetValue(
                    __instance,
                    _ => PaperVisualState.Capture(background));
                state.Restore(background);

                if (Cfg.PaperCfgMap != null)
                {
                    _registry.ValidateOriginalIds(Cfg.PaperCfgMap.Keys);
                }

                if (__instance.parms == null || __instance.parms.Length == 0)
                {
                    return;
                }

                int paperId = Convert.ToInt32(__instance.parms[0]);
                if (!_registry.TryGet(paperId, out RegisteredScreenPaper paper))
                {
                    return;
                }

                if (!_services.TextureCache.TryGetSprite(paper.ImagePath, out Sprite sprite))
                {
                    _registry.ReportBrokenImage(paper);
                    return;
                }

                background.sprite = sprite;
                background.overrideSprite = sprite;
                background.color = Color.white;
                background.type = Image.Type.Simple;
                background.preserveAspect = false;
                PatchLog.Info(
                    $"屏幕特效模块-5001屏幕纸条调用：paperId={paperId}, image={paper.ImagePath}");
            }
            catch (Exception exception)
            {
                PatchLog.Warning(
                    $"5001屏幕纸条扩展-应用图片失败，已保留原版纸条：{ModuleHost.GetReason(exception)}");
            }
        }

        private sealed class PaperVisualState
        {
            private readonly Sprite _sprite;
            private readonly Sprite _overrideSprite;
            private readonly Color _color;
            private readonly Image.Type _type;
            private readonly bool _preserveAspect;

            private PaperVisualState(
                Sprite sprite,
                Sprite overrideSprite,
                Color color,
                Image.Type type,
                bool preserveAspect)
            {
                _sprite = sprite;
                _overrideSprite = overrideSprite;
                _color = color;
                _type = type;
                _preserveAspect = preserveAspect;
            }

            internal static PaperVisualState Capture(Image image)
            {
                return new PaperVisualState(
                    image.sprite,
                    image.overrideSprite,
                    image.color,
                    image.type,
                    image.preserveAspect);
            }

            internal void Restore(Image image)
            {
                image.sprite = _sprite;
                image.overrideSprite = _overrideSprite;
                image.color = _color;
                image.type = _type;
                image.preserveAspect = _preserveAspect;
            }
        }
    }
}
