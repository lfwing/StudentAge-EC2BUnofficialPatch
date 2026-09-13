using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Config;
using EC2BUnofficialPatch.Core;
using GenUI.Main;
using HarmonyLib;
using Sdk;
using View.Evt;
using View.Main;

namespace EC2BUnofficialPatch.Features.Optimization.CGOptimization
{
    /// <summary>
    /// 优化剧情内 4015 全屏 CG 的连续播放，并为 Mod CG 图鉴提供确定性排序。
    /// 多 URL CG 继续遵循原版数据模型：一个 CGCfg 对应一个图鉴条目。
    /// </summary>
    internal sealed class CGOptimizationModule : IPluginModule
    {
        private static readonly IReadOnlyList<ModuleLogItem> Items = new[]
        {
            new ModuleLogItem("优化", "CG播放与图鉴排序优化")
        };

        public string Key => "cg-optimization";

        public IReadOnlyList<ModuleLogItem> LogItems => Items;

        public void Load(Harmony harmony, PluginServices services)
        {
            PatchRequired(
                harmony,
                typeof(CGView),
                "ShowCG",
                new[] { typeof(int) },
                prefix: nameof(CGOptimizationPatches.ShowCgPrefix));

            PatchRequired(
                harmony,
                typeof(CGView),
                "ShowMiniCG",
                new[] { typeof(int) },
                prefix: nameof(CGOptimizationPatches.ShowMiniCgPrefix));

            PatchRequired(
                harmony,
                typeof(NewTalkView),
                "ShowComic",
                new[] { typeof(int), typeof(int), typeof(Action) },
                prefix: nameof(CGOptimizationPatches.ShowOtherCgModePrefix));

            PatchRequired(
                harmony,
                typeof(PreviewTalkView),
                "ShowComic",
                new[] { typeof(int), typeof(int), typeof(Action) },
                prefix: nameof(CGOptimizationPatches.ShowOtherCgModePrefix));

            PatchRequired(
                harmony,
                typeof(NewTalkView),
                "HideComic",
                Type.EmptyTypes,
                prefix: nameof(CGOptimizationPatches.HideComicPrefix));

            PatchRequired(
                harmony,
                typeof(PreviewTalkView),
                "HideComic",
                Type.EmptyTypes,
                prefix: nameof(CGOptimizationPatches.HideComicPrefix));

            PatchRequired(
                harmony,
                typeof(NewTalkView),
                "HideCGComic",
                Type.EmptyTypes,
                prefix: nameof(CGOptimizationPatches.HideCgComicPrefix));

            PatchRequired(
                harmony,
                typeof(PreviewTalkView),
                "HideCGComic",
                Type.EmptyTypes,
                prefix: nameof(CGOptimizationPatches.HideCgComicPrefix));

            PatchRequired(
                harmony,
                typeof(CGLibraryView),
                nameof(CGLibraryView.OnOpen),
                Type.EmptyTypes,
                postfix: nameof(CGOptimizationPatches.LibraryOnOpenPostfix));

            PatchRequired(
                harmony,
                typeof(CGLibraryView),
                "OnRenderCG",
                new[] { typeof(UICell) },
                postfix: nameof(CGOptimizationPatches.LibraryOnRenderCgPostfix));

            PatchLog.Debug(
                "优化模块-CG优化补丁安装完成：" +
                "4015双层交叉淡化+独立保底层；" +
                "4017/漫画/迷你CG生命周期清理；" +
                "group3按ID排序并连续编号");
        }

        private static void PatchRequired(
            Harmony harmony,
            Type declaringType,
            string methodName,
            Type[] argumentTypes,
            string prefix = null,
            string postfix = null)
        {
            MethodInfo target = AccessTools.Method(declaringType, methodName, argumentTypes);
            if (target == null)
            {
                throw new MissingMethodException(
                    $"未找到 {declaringType.FullName}.{methodName}({FormatArguments(argumentTypes)})。");
            }

            HarmonyMethod prefixMethod = string.IsNullOrEmpty(prefix)
                ? null
                : new HarmonyMethod(typeof(CGOptimizationPatches), prefix);

            HarmonyMethod postfixMethod = string.IsNullOrEmpty(postfix)
                ? null
                : new HarmonyMethod(typeof(CGOptimizationPatches), postfix);

            harmony.Patch(target, prefix: prefixMethod, postfix: postfixMethod);
        }

        private static string FormatArguments(Type[] argumentTypes)
        {
            if (argumentTypes == null || argumentTypes.Length == 0)
            {
                return string.Empty;
            }

            string[] names = new string[argumentTypes.Length];
            for (int index = 0; index < argumentTypes.Length; index++)
            {
                names[index] = argumentTypes[index]?.Name ?? "<null>";
            }

            return string.Join(", ", names);
        }
    }

    internal static class CGOptimizationPatches
    {
        private const int ModCfgGroup = 3;
        private const float HideComicDuration = 0.1f;
        private const float HideCgComicDuration = 0.5f;

        private static readonly FieldInfo CgCfgMapField =
            AccessTools.Field(typeof(CGView), "cgCfgMap");

        private static readonly FieldInfo AllCgsField =
            AccessTools.Field(typeof(CGLibraryView), "allCGs");

        private static readonly ConditionalWeakTable<CGLibraryView, ModCgIndexMap>
            DisplayIndexMaps = new ConditionalWeakTable<CGLibraryView, ModCgIndexMap>();

        internal static bool ShowCgPrefix(CGView __instance, int _id)
        {
            CGTransitionController controller = null;
            try
            {
                if (__instance == null || __instance.icon_cg == null ||
                    __instance.icon_cg.gameObject == null || __instance.icon_cg.image == null)
                {
                    PatchLog.Warning(
                        $"优化模块-CG优化无法接管剧情 CG：cgId={_id}, reason=CGView 或 icon_cg 无效");
                    return true;
                }

                Dictionary<int, CGCfg> map =
                    CgCfgMapField?.GetValue(__instance) as Dictionary<int, CGCfg> ?? Cfg.CGCfgMap;

                if (map == null || !map.TryGetValue(_id, out CGCfg cfg) ||
                    cfg == null || cfg.urls == null || cfg.urls.Count == 0)
                {
                    PatchLog.Error(
                        $"优化模块-CG优化找不到有效 CG 配置，回退原版：cgId={_id}");
                    return true;
                }

                string url = cfg.GetImgUrl(GenderDefine.Unknown);
                if (string.IsNullOrEmpty(url))
                {
                    PatchLog.Error(
                        $"优化模块-CG优化 CG URL 为空，回退原版：cgId={_id}");
                    return true;
                }

                __instance.icon_cg_mini?.gameObject?.SetActive(false);
                __instance.icon_cg.gameObject.SetActive(true);

                controller =
                    __instance.icon_cg.gameObject.GetComponent<CGTransitionController>() ??
                    __instance.icon_cg.gameObject.AddComponent<CGTransitionController>();

                controller.Bind(__instance.icon_cg);
                controller.Play(_id, url);

                if (Game.GetGameState() == GameState.Running)
                {
                    Singleton<GlobalMgr>.Ins.AddCG(url);
                }

                return false;
            }
            catch (Exception exception)
            {
                controller?.FallbackToOriginal("ShowCG 接管异常");
                PatchLog.Exception(
                    $"优化模块-CG优化接管剧情 CG 失败，回退原版：cgId={_id}",
                    exception);
                return true;
            }
        }

        internal static void ShowMiniCgPrefix(CGView __instance, int _id)
        {
            try
            {
                CGTransitionController controller =
                    __instance?.icon_cg?.gameObject?.GetComponent<CGTransitionController>();

                controller?.SwitchToOtherMode($"切换到迷你CG：cgId={_id}");
            }
            catch (Exception exception)
            {
                PatchLog.Exception("优化模块-CG优化切换迷你CG时清理保底层失败", exception);
            }
        }

        internal static void ShowOtherCgModePrefix()
        {
            CGTransitionController.ClearAllImmediately("切换到漫画模式");
        }

        internal static void HideComicPrefix()
        {
            CGTransitionController.BeginExitAll(HideComicDuration, "HideComic");
        }

        internal static void HideCgComicPrefix()
        {
            CGTransitionController.BeginExitAll(HideCgComicDuration, "HideCGComic/4017");
        }

        internal static void LibraryOnOpenPostfix(CGLibraryView __instance)
        {
            try
            {
                if (__instance == null)
                {
                    return;
                }

                List<CGLibData> allCgs =
                    AllCgsField?.GetValue(__instance) as List<CGLibData>;

                if (allCgs == null)
                {
                    PatchLog.Warning("优化模块-CG优化图鉴 allCGs 为空，跳过排序");
                    ReplaceDisplayIndexMap(__instance, new Dictionary<int, int>());
                    return;
                }

                List<int> modPositions = new List<int>();
                List<CGLibData> modEntries = new List<CGLibData>();

                for (int index = 0; index < allCgs.Count; index++)
                {
                    CGLibData data = allCgs[index];
                    CGCfg cfg = GetCfg(data.id);
                    if (cfg?.group != ModCfgGroup)
                    {
                        continue;
                    }

                    modPositions.Add(index);
                    modEntries.Add(data);
                }

                modEntries.Sort(CompareModEntriesById);

                Dictionary<int, int> displayIndexes = new Dictionary<int, int>();
                for (int index = 0; index < modEntries.Count; index++)
                {
                    CGLibData data = modEntries[index];
                    allCgs[modPositions[index]] = data;
                    displayIndexes[data.id] = index + 1;
                }

                ReplaceDisplayIndexMap(__instance, displayIndexes);

                PatchLog.Debug(
                    "优化模块-CG优化图鉴排序完成：" +
                    $"total={allCgs.Count}, modGroup3={modEntries.Count}, " +
                    "rule=仅group3按CG ID升序；displayIdx=001起连续编号；" +
                    "officialGroups=保持原版相对顺序；multiUrl=一个CGCfg一个条目");

                for (int index = 0; index < modEntries.Count; index++)
                {
                    CGLibData data = modEntries[index];
                    CGCfg cfg = GetCfg(data.id);
                    PatchLog.Debug(
                        "优化模块-CG优化Mod图鉴排序项：" +
                        $"position={index + 1}, displayIdx={index + 1:D3}, " +
                        $"id={data.id}, originalIdx={cfg?.idx ?? 0}, firstUrl={GetFirstUrl(cfg)}");
                }

                // 原版 OnOpen 已可能按旧顺序完成首次数据刷新；重新刷新确保当前页立即使用新顺序。
                __instance.Refresh();
            }
            catch (Exception exception)
            {
                PatchLog.Exception("优化模块-CG优化图鉴排序失败，保留原版顺序和编号", exception);
            }
        }

        internal static void LibraryOnRenderCgPostfix(CGLibraryView __instance, UICell _cell)
        {
            try
            {
                if (__instance == null || !(_cell is Cell_CGLibraryItemUI cell) ||
                    !(cell.data is CGLibData data))
                {
                    return;
                }

                CGCfg cfg = GetCfg(data.id);
                if (cfg?.group != ModCfgGroup)
                {
                    return;
                }

                if (!DisplayIndexMaps.TryGetValue(__instance, out ModCgIndexMap indexMap) ||
                    !indexMap.Indexes.TryGetValue(data.id, out int displayIndex))
                {
                    PatchLog.Warning(
                        $"优化模块-CG优化未找到Mod CG运行时编号：id={data.id}");
                    return;
                }

                cell.txt_idx.text = displayIndex.ToString("D3");
            }
            catch (Exception exception)
            {
                PatchLog.Exception("优化模块-CG优化覆盖Mod CG显示编号失败", exception);
            }
        }

        private static void ReplaceDisplayIndexMap(
            CGLibraryView view,
            Dictionary<int, int> indexes)
        {
            DisplayIndexMaps.Remove(view);
            DisplayIndexMaps.Add(view, new ModCgIndexMap(indexes));
        }

        private static int CompareModEntriesById(CGLibData left, CGLibData right)
        {
            int result = CompareInt(left.id, right.id);
            if (result != 0)
            {
                return result;
            }

            return StringComparer.Ordinal.Compare(
                GetFirstUrl(GetCfg(left.id)),
                GetFirstUrl(GetCfg(right.id)));
        }

        private static CGCfg GetCfg(int id)
        {
            if (Cfg.CGCfgMap != null && Cfg.CGCfgMap.TryGetValue(id, out CGCfg cfg))
            {
                return cfg;
            }

            return null;
        }

        private static int CompareInt(int left, int right)
        {
            return left < right ? -1 : left > right ? 1 : 0;
        }

        private static string GetFirstUrl(CGCfg cfg)
        {
            return cfg?.urls != null && cfg.urls.Count > 0
                ? cfg.urls[0] ?? string.Empty
                : string.Empty;
        }

        private sealed class ModCgIndexMap
        {
            internal ModCgIndexMap(Dictionary<int, int> indexes)
            {
                Indexes = indexes ?? new Dictionary<int, int>();
            }

            internal Dictionary<int, int> Indexes { get; }
        }
    }
}
