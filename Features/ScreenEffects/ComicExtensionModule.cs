using System;
using System.Collections.Generic;
using System.Reflection;
using Config;
using EC2BUnofficialPatch.Core;
using GenUI.Talk;
using HarmonyLib;
using Sdk;
using UnityEngine;
using View.Evt;

namespace EC2BUnofficialPatch.Features.ScreenEffects
{
    internal sealed class ComicExtensionModule : IPluginModule
    {
        private static PluginServices _services;
        private static readonly IReadOnlyList<ModuleLogItem> Items = new[]
        {
            new ModuleLogItem("屏幕特效", "4016-漫画显示扩展")
        };

        public string Key => "screen.4016";
        public IReadOnlyList<ModuleLogItem> LogItems => Items;

        public void Load(Harmony harmony, PluginServices services)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _services.ComicResources.ReportScanIssues();

            MethodInfo render = AccessTools.Method(typeof(ComicView), "OnRender")
                ?? throw new MissingMethodException(typeof(ComicView).FullName, "OnRender");
            MethodInfo play = AccessTools.Method(typeof(ComicView), "Play", new[] { typeof(int), typeof(int) })
                ?? throw new MissingMethodException(typeof(ComicView).FullName, "Play(int,int)");

            harmony.Patch(render, prefix: new HarmonyMethod(typeof(ComicExtensionModule), nameof(OnRenderPrefix)));
            harmony.Patch(play, prefix: new HarmonyMethod(typeof(ComicExtensionModule), nameof(PlayPrefix)));

            PatchLog.Registration(
                "屏幕特效模块-4016漫画外置资源索引完成：" +
                $"comic目录={_services.ComicResources.DirectoryCount}, " +
                $"有效图片={_services.ComicResources.ImageCount}, " +
                $"不规范文件={_services.ComicResources.InvalidFileCount}, " +
                $"冲突={_services.ComicResources.ConflictCount}");
        }

        private static void PlayPrefix(int __0, int __1)
        {
            if (_services?.ComicResources == null)
                return;
            try
            {
                if (Cfg.CGCfgMap.TryGetValue(__0, out CGCfg cfg))
                {
                    _services.ComicResources.ValidateCfg(cfg);
                    if (cfg.urls != null && cfg.urls.Exists(_services.ComicResources.IsExternalComicUrl))
                        PatchLog.Info($"屏幕特效模块-4016外置漫画调用：cg={__0}, startPage={__1}");
                }
            }
            catch (Exception exception)
            {
                PatchLog.Error($"屏幕特效模块-4016漫画配置校验异常：cg={__0}, reason={ModuleHost.GetReason(exception)}");
            }
        }

        private static bool OnRenderPrefix(UICell __0)
        {
            Cell_ComicItemUI cell = __0 as Cell_ComicItemUI;
            string url = cell?.data as string;
            if (cell?.icon_item?.image == null || _services?.ComicResources == null)
                return true;

            if (!_services.ComicResources.IsExternalComicUrl(url))
                return true;

            if (!_services.ComicResources.TryResolve(url, out string fullPath, out string reason))
            {
                _services.ComicResources.ReportResolutionIssueOnce(url, reason);
                cell.icon_item.Clear();
                return false;
            }

            if (!_services.TextureCache.TryGetSprite(fullPath, out Sprite sprite))
            {
                _services.ComicResources.ReportResolutionIssueOnce(url, "图片文件存在，但 Texture2D/Sprite 解码失败：" + fullPath);
                cell.icon_item.Clear();
                return false;
            }

            cell.icon_item.showWhenComp = true;
            cell.icon_item.image.color = Color.white;
            cell.icon_item.SetSprite(sprite);
            return false;
        }
    }
}
