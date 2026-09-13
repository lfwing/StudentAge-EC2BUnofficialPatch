using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EC2BUnofficialPatch.Core;
using EC2BUnofficialPatch.Features.Mechanics.Minigames;
using HarmonyLib;

namespace EC2BUnofficialPatch.Features.Mechanics
{
    internal sealed class MechanicsModule : IPluginModule
    {
        private static readonly IReadOnlyList<ModuleLogItem> Items = new[]
        {
            new ModuleLogItem("机制", "角色独立小游戏阶段"),
            new ModuleLogItem("机制", "四类自定义小游戏自动注册"),
            new ModuleLogItem("机制", "社交小游戏统一结算")
        };

        public string Key => "mechanics.minigame";

        public IReadOnlyList<ModuleLogItem> LogItems => Items;

        public void Load(Harmony harmony, PluginServices services)
        {
            CustomMinigameRegistry registry =
                CustomMinigameRegistry.Load(services.ContentRoots);
            MiniGameMechanicsPatches.Initialize(registry);

            Patch(
                harmony,
                RequireMethod(typeof(MiniGameData), "SocialGame", typeof(int), typeof(int)),
                prefix: nameof(MiniGameMechanicsPatches.SocialGamePrefix));
            Patch(
                harmony,
                RequireMethod(typeof(MiniGameData), "GetGameByNpc", typeof(int)),
                prefix: nameof(MiniGameMechanicsPatches.GetGameByNpcPrefix));
            Patch(
                harmony,
                RequireMethod(typeof(MiniGameData), "GetGame", typeof(int)),
                prefix: nameof(MiniGameMechanicsPatches.GetGamePrefix));

            Patch(
                harmony,
                RequireMethod(
                    typeof(FuncMgr),
                    "OpenMiniGame",
                    typeof(int),
                    typeof(MiniGameFromType),
                    typeof(int),
                    typeof(List<double>),
                    typeof(Action),
                    typeof(Action),
                    typeof(Action<float>),
                    typeof(int)),
                prefix: nameof(MiniGameMechanicsPatches.OpenMiniGamePrefix),
                postfix: nameof(MiniGameMechanicsPatches.OpenMiniGamePostfix),
                finalizer: nameof(MiniGameMechanicsPatches.OpenMiniGameFinalizer));

            Patch(
                harmony,
                RequireMethod(
                    typeof(MiniGameData),
                    "EndGame",
                    typeof(int),
                    typeof(bool),
                    typeof(int)),
                prefix: nameof(MiniGameMechanicsPatches.EndGamePrefix),
                postfix: nameof(MiniGameMechanicsPatches.EndGamePostfix),
                finalizer: nameof(MiniGameMechanicsPatches.EndGameFinalizer));

            Patch(
                harmony,
                RequireMethod(
                    typeof(GlobalMgr),
                    "AddMinigameToHistory",
                    typeof(int),
                    typeof(int),
                    typeof(int)),
                prefix: nameof(MiniGameMechanicsPatches.HistoryIdPrefix));
            Patch(
                harmony,
                RequireMethod(
                    typeof(GlobalMgr),
                    "HasMinigamePlayed",
                    typeof(int),
                    typeof(int),
                    typeof(int)),
                prefix: nameof(MiniGameMechanicsPatches.HistoryIdPrefix));

            Patch(
                harmony,
                RequireMethod(
                    typeof(CommonEvtMgr),
                    "ShowTalk",
                    typeof(int),
                    typeof(Action),
                    typeof(int),
                    typeof(bool),
                    typeof(bool),
                    typeof(string)),
                prefix: nameof(MiniGameMechanicsPatches.ShowTalkPrefix));

            PatchConcreteViews(harmony);

            if (!MiniGameStageValidator.TryLogDefinitionProblems(registry))
            {
                PatchLog.Debug(
                    "机制模块-游戏 cfg 尚未初始化，阶段定义检查已延迟到首次社交小游戏启动时执行");
            }

            PatchLog.Registration(
                "机制模块-小游戏阶段机制初始化完成：" +
                $"explicitMappings={registry.ExplicitCount}, " +
                $"dispatchers={OriginalMinigameCatalog.All.Count()}, " +
                $"closeObserved={OriginalMinigameCatalog.All.Count(item => item.NeedsCloseObservation)}");
        }

        private static void PatchConcreteViews(Harmony harmony)
        {
            HashSet<MethodBase> patched = new HashSet<MethodBase>();
            foreach (OriginalMinigameDescriptor descriptor in OriginalMinigameCatalog.All)
            {
                if (!descriptor.NeedsCloseObservation)
                {
                    continue;
                }

                Type viewType = descriptor.ResolveViewType();
                if (viewType == null)
                {
                    throw new TypeLoadException(
                        $"找不到小游戏 View：implementation={descriptor.Id}, type={descriptor.ViewTypeName}");
                }

                MethodInfo closeView = FindImplementedMethod(viewType, "CloseView", Type.EmptyTypes);
                if (closeView == null)
                {
                    PatchLog.Warning($"机制模块-小游戏适配器未找到 CloseView 实现：id={descriptor.Id}, view={viewType.FullName}");
                    continue;
                }

                if (!patched.Add(closeView))
                {
                    continue;
                }

                Patch(
                    harmony,
                    closeView,
                    prefix: nameof(MiniGameMechanicsPatches.ConcreteClosePrefix),
                    postfix: nameof(MiniGameMechanicsPatches.ConcreteClosePostfix),
                    finalizer: nameof(MiniGameMechanicsPatches.ConcreteCloseFinalizer));
            }
        }


        /// <summary>
        /// 从具体 View 类型开始向基类查找真正声明/实现目标方法的类型。
        /// 不能直接用 AccessTools.Method(viewType, ...)：当方法只是继承而未在 viewType
        /// 中声明时，HarmonyX 会把带有派生 ReflectedType 的 MethodInfo 判定为“补丁继承方法”，
        /// 并给出 should only patch implemented methods/constructors 警告。
        /// </summary>
        private static MethodInfo FindImplementedMethod(Type type, string name, Type[] parameterTypes)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                MethodInfo method = current.GetMethod(
                    name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null,
                    parameterTypes ?? Type.EmptyTypes,
                    null);

                if (method != null && !method.IsAbstract)
                    return method;
            }

            return null;
        }

        private static void Patch(
            Harmony harmony,
            MethodBase target,
            string prefix = null,
            string postfix = null,
            string finalizer = null)
        {
            harmony.Patch(
                target,
                prefix: ToHarmonyMethod(prefix),
                postfix: ToHarmonyMethod(postfix),
                finalizer: ToHarmonyMethod(finalizer));
        }

        private static HarmonyMethod ToHarmonyMethod(string methodName) =>
            string.IsNullOrWhiteSpace(methodName)
                ? null
                : new HarmonyMethod(typeof(MiniGameMechanicsPatches), methodName);

        private static MethodInfo RequireMethod(
            Type type,
            string name,
            params Type[] parameterTypes)
        {
            return AccessTools.Method(type, name, parameterTypes)
                ?? throw new MissingMethodException(type.FullName, name);
        }
    }
}
