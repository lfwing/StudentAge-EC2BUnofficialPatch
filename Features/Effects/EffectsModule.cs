using System.Collections.Generic;
using EC2BUnofficialPatch.Core;
using HarmonyLib;

namespace EC2BUnofficialPatch.Features.Effects
{
    internal sealed class EffectsModule : IPluginModule
    {
        private static readonly IReadOnlyList<ModuleLogItem> Items = new[]
        {
            new ModuleLogItem("效果", "空模块架构")
        };

        public string Key => "effects";

        public IReadOnlyList<ModuleLogItem> LogItems => Items;

        public void Load(Harmony harmony, PluginServices services)
        {
        }
    }
}
