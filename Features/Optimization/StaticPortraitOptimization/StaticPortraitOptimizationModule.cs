using System;
using System.Collections.Generic;
using Config;
using EC2BUnofficialPatch.Core;
using GenUI.Common;
using HarmonyLib;
using Sdk;
using UnityEngine;
using View.Common;

namespace EC2BUnofficialPatch.Features.Optimization.StaticPortraitOptimization
{
    /// <summary>
    /// 静态立绘优化：
    /// 1. 在 UISprite 发起请求时接管人物静态立绘加载；
    /// 2. 使用请求代次阻止旧异步回调覆盖新表情；
    /// 3. 在 icon_role 自身 CanvasGroup 内使用双层 Image 交叉淡化；
    /// 4. 不修改人物的缩放、位置、锚点和素材尺寸。
    /// </summary>
    internal sealed class StaticPortraitOptimizationModule : IPluginModule
    {
        private static readonly IReadOnlyList<ModuleLogItem> Items = new[]
        {
            new ModuleLogItem("优化", "静态立绘优化")
        };

        public string Key => "static-portrait-optimization";

        public IReadOnlyList<ModuleLogItem> LogItems => Items;

        public void Load(Harmony harmony, PluginServices services)
        {
            PatchLog.Debug("优化模块-静态立绘优化开始安装重构补丁");

            harmony.Patch(
                AccessTools.Method(typeof(TalkRoleItem), nameof(TalkRoleItem.OnCellCreate)),
                postfix: new HarmonyMethod(
                    typeof(StaticPortraitOptimizationPatches),
                    nameof(StaticPortraitOptimizationPatches.OnCellCreatePostfix)));

            harmony.Patch(
                AccessTools.Method(typeof(TalkRoleItem), nameof(TalkRoleItem.OnCellRecycle)),
                prefix: new HarmonyMethod(
                    typeof(StaticPortraitOptimizationPatches),
                    nameof(StaticPortraitOptimizationPatches.OnCellRecyclePrefix)));

            harmony.Patch(
                AccessTools.Method(
                    typeof(TalkRoleItem),
                    nameof(TalkRoleItem.SetData),
                    new[]
                    {
                        typeof(PersonCfg), typeof(UICell), typeof(int), typeof(int), typeof(float), typeof(int),
                        typeof(bool), typeof(int), typeof(int), typeof(int), typeof(int), typeof(GenderDefine),
                        typeof(L2DLoadType)
                    }),
                prefix: new HarmonyMethod(
                    typeof(StaticPortraitOptimizationPatches),
                    nameof(StaticPortraitOptimizationPatches.SetDataPrefix)));

            harmony.Patch(
                AccessTools.Method(
                    typeof(UISprite),
                    nameof(UISprite.SetTextureUrl),
                    new[] { typeof(string), typeof(bool) }),
                prefix: new HarmonyMethod(
                    typeof(StaticPortraitOptimizationPatches),
                    nameof(StaticPortraitOptimizationPatches.SetTextureUrlPrefix)));

            harmony.Patch(
                AccessTools.Method(
                    typeof(UISprite),
                    nameof(UISprite.SetAtlasUrl),
                    new[] { typeof(string), typeof(bool) }),
                prefix: new HarmonyMethod(
                    typeof(StaticPortraitOptimizationPatches),
                    nameof(StaticPortraitOptimizationPatches.SetAtlasUrlPrefix)));

            harmony.Patch(
                AccessTools.Method(
                    typeof(UISprite),
                    nameof(UISprite.SetExternTextureUrl),
                    new[] { typeof(string), typeof(bool) }),
                prefix: new HarmonyMethod(
                    typeof(StaticPortraitOptimizationPatches),
                    nameof(StaticPortraitOptimizationPatches.SetExternTextureUrlPrefix)));

            harmony.Patch(
                AccessTools.Method(
                    typeof(UISprite),
                    nameof(UISprite.SetSprite),
                    new[] { typeof(Sprite) }),
                prefix: new HarmonyMethod(
                    typeof(StaticPortraitOptimizationPatches),
                    nameof(StaticPortraitOptimizationPatches.SetSpritePrefix)));

            harmony.Patch(
                AccessTools.Method(typeof(UISprite), nameof(UISprite.Clear)),
                prefix: new HarmonyMethod(
                    typeof(StaticPortraitOptimizationPatches),
                    nameof(StaticPortraitOptimizationPatches.ClearPrefix)));

            PatchLog.Debug(
                "优化模块-静态立绘优化重构补丁安装完成：" +
                "TalkRoleItem.OnCellCreate/OnCellRecycle/SetData；" +
                "UISprite.SetTextureUrl/SetAtlasUrl/SetExternTextureUrl/SetSprite/Clear");
        }
    }

    internal static class StaticPortraitOptimizationPatches
    {
        internal static void OnCellCreatePostfix(UICell _cell)
        {
            try
            {
                Cell_NewTalkRoleItemUI cell = _cell as Cell_NewTalkRoleItemUI;
                UISprite icon = cell?.icon_role;
                if (icon?.gameObject == null || icon.image == null || icon.transform == null)
                {
                    PatchLog.Warning("优化模块-静态立绘 OnCellCreate 未找到有效 icon_role，跳过绑定");
                    return;
                }

                StaticPortraitTransition transition =
                    icon.gameObject.GetComponent<StaticPortraitTransition>() ??
                    icon.gameObject.AddComponent<StaticPortraitTransition>();

                transition.Bind(icon, cell.canvasgroup_role);
                PatchLog.Debug(
                    "优化模块-静态立绘绑定完成：" +
                    $"cellType={cell.GetType().FullName}, iconObject={icon.gameObject.name}, " +
                    $"spriteId={icon.GetHashCode()}");
            }
            catch (Exception exception)
            {
                PatchLog.Exception("优化模块-静态立绘 OnCellCreate 绑定失败", exception);
            }
        }

        internal static void OnCellRecyclePrefix(UICell _cell)
        {
            try
            {
                Cell_NewTalkRoleItemUI cell = _cell as Cell_NewTalkRoleItemUI;
                GetTransition(cell?.icon_role)?.Recycle();
            }
            catch (Exception exception)
            {
                PatchLog.Exception("优化模块-静态立绘 OnCellRecycle 重置失败", exception);
            }
        }

        internal static void SetDataPrefix(
            PersonCfg _cfg,
            UICell _cell,
            int _order,
            int _colorId,
            float _alpha,
            int _cloth,
            bool _lookAtMouse,
            int _exp,
            int _flip,
            int _hair,
            int _gradeState,
            GenderDefine _gender,
            L2DLoadType _loadType)
        {
            StaticPortraitTransition transition = null;
            try
            {
                Cell_NewTalkRoleItemUI cell = _cell as Cell_NewTalkRoleItemUI;
                transition = GetTransition(cell?.icon_role);
                if (transition == null || _cfg == null)
                {
                    return;
                }

                int gradeState = _gradeState == -1
                    ? Singleton<RoleMgr>.Ins.GetRole().GradeState
                    : _gradeState;

                bool isStaticPortrait = _cfg.IsUseImg(gradeState, _cloth);
                transition.Configure(
                    isStaticPortrait,
                    _cfg.id,
                    _cloth,
                    gradeState);

                PatchLog.Debug(
                    "优化模块-静态立绘 SetData 前置配置：" +
                    $"role={_cfg.id}, cloth={_cloth}, expression={_exp}, grade={gradeState}, " +
                    $"static={isStaticPortrait}, alpha={_alpha:F2}, flip={_flip}");
            }
            catch (Exception exception)
            {
                transition?.FallbackToOriginal("SetData 前置配置异常");
                PatchLog.Exception("优化模块-静态立绘 SetData 前置配置失败，已回退原版", exception);
            }
        }

        internal static bool SetTextureUrlPrefix(
            UISprite __instance,
            string _url,
            bool _showWhenComp)
        {
            StaticPortraitTransition transition = GetTransition(__instance);
            if (transition == null)
            {
                return true;
            }

            try
            {
                return !transition.TryHandleTextureRequest(_url, _showWhenComp);
            }
            catch (Exception exception)
            {
                transition.FallbackToOriginal("SetTextureUrl 接管异常");
                PatchLog.Exception("优化模块-静态立绘接管 SetTextureUrl 失败，已回退原版", exception);
                return true;
            }
        }

        internal static bool SetAtlasUrlPrefix(
            UISprite __instance,
            string _url,
            bool _showWhenComp)
        {
            StaticPortraitTransition transition = GetTransition(__instance);
            if (transition == null)
            {
                return true;
            }

            try
            {
                return !transition.TryHandleAtlasRequest(_url, _showWhenComp);
            }
            catch (Exception exception)
            {
                transition.FallbackToOriginal("SetAtlasUrl 接管异常");
                PatchLog.Exception("优化模块-静态立绘接管 SetAtlasUrl 失败，已回退原版", exception);
                return true;
            }
        }

        internal static bool SetExternTextureUrlPrefix(
            UISprite __instance,
            string _url,
            bool _isReload)
        {
            StaticPortraitTransition transition = GetTransition(__instance);
            if (transition == null)
            {
                return true;
            }

            try
            {
                return !transition.TryHandleExternalRequest(_url, _isReload);
            }
            catch (Exception exception)
            {
                transition.FallbackToOriginal("SetExternTextureUrl 接管异常");
                PatchLog.Exception("优化模块-静态立绘接管 SetExternTextureUrl 失败，已回退原版", exception);
                return true;
            }
        }

        internal static bool SetSpritePrefix(UISprite __instance, Sprite _sprite)
        {
            StaticPortraitTransition transition = GetTransition(__instance);
            if (transition == null || !transition.IsProxyActive)
            {
                return true;
            }

            // 本模块自己的异步回调不会调用 UISprite.SetSprite。
            // 因此静态立绘代理启用期间进入这里的调用均属于原版残留/过期回调，必须阻止。
            transition.RejectUnexpectedSetSprite(_sprite);
            return false;
        }

        internal static bool ClearPrefix(UISprite __instance)
        {
            StaticPortraitTransition transition = GetTransition(__instance);
            if (transition == null)
            {
                return true;
            }

            transition.ClearFromGameCall();
            return false;
        }

        private static StaticPortraitTransition GetTransition(UISprite sprite)
        {
            return sprite?.gameObject?.GetComponent<StaticPortraitTransition>();
        }
    }
}
