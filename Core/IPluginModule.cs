using System.Collections.Generic;
using HarmonyLib;

namespace EC2BUnofficialPatch.Core
{
    internal interface IPluginModule
    {
        string Key { get; }

        IReadOnlyList<ModuleLogItem> LogItems { get; }

        void Load(Harmony harmony, PluginServices services);
    }

    internal sealed class ModuleLogItem
    {
        internal ModuleLogItem(string category, string feature)
        {
            Category = category;
            Feature = feature;
        }

        internal string Category { get; }

        internal string Feature { get; }
    }
}
