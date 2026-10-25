using System.Collections.Generic;
using EC2BUnofficialPatch.Core;
using HarmonyLib;
using EC2BUnofficialPatch.Features.Mechanics.MapExtend;

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
            MapExtendEffects.InstallMove(harmony);
        }

    }
}
