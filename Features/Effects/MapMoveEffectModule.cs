using System;
using System.Collections.Generic;
using System.Reflection;
using EC2BUnofficialPatch.Core;
using Effect;
using HarmonyLib;

namespace EC2BUnofficialPatch.Features.Effects
{
    internal sealed class MapMoveEffectModule : IPluginModule
    {
        private static readonly IReadOnlyList<ModuleLogItem> Items = new[]
        {
            new ModuleLogItem("效果", "100,1地点移动修复")
        };

        public string Key => "effects.map-move";

        public IReadOnlyList<ModuleLogItem> LogItems => Items;

        public void Load(Harmony harmony, PluginServices services)
        {
            Patch(
                harmony,
                AccessTools.Method(typeof(EffectorUseful), nameof(EffectorUseful.OnRun)),
                prefix: AccessTools.Method(
                    typeof(MapMoveEffectPatches),
                    nameof(MapMoveEffectPatches.OnRunPrefix)));
            Patch(
                harmony,
                AccessTools.Method(typeof(EffectorUseful), nameof(EffectorUseful.OnToString)),
                prefix: AccessTools.Method(
                    typeof(MapMoveEffectPatches),
                    nameof(MapMoveEffectPatches.OnToStringPrefix)));
        }

        private static void Patch(
            Harmony harmony,
            MethodInfo target,
            MethodInfo prefix)
        {
            if (target == null || prefix == null)
            {
                throw new MissingMethodException("移动到地点效果所需的游戏方法不存在。");
            }

            harmony.Patch(target, new HarmonyMethod(prefix));
        }
    }
}
