using System;
using System.Collections.Generic;
using System.Reflection;
using EC2BUnofficialPatch.Core;
using EC2BUnofficialPatch.Services;
using HarmonyLib;

namespace EC2BUnofficialPatch.Features.Optimization
{
    internal sealed class RelationFocusCountModule : IPluginModule
    {
        private static readonly IReadOnlyList<ModuleLogItem> Items = new[]
        {
            new ModuleLogItem("优化", "关注人数统计优化")
        };

        public string Key => "optimization.relation-focus-count";
        public IReadOnlyList<ModuleLogItem> LogItems => Items;

        public void Load(Harmony harmony, PluginServices services)
        {
            // 1.93 中 CheckOldSave 属于 RelationData，而不是 RoleMgr。
            Patch(harmony, AccessTools.Method(typeof(RelationData), "CheckOldSave"), postfix: true);
            Patch(harmony, AccessTools.Method(typeof(RelationData), "ChangeRelation"), postfix: true);
            Patch(harmony, AccessTools.Method(typeof(RelationData), "MakeAcquaintances"), postfix: true);
            Patch(harmony, AccessTools.Method(typeof(RelationData), "UnFocus"), postfix: true);
            Patch(harmony, AccessTools.Method(typeof(RelationData), "ReFocusNpc"), postfix: true);
            Patch(harmony, AccessTools.Method(typeof(RelationData), "GetSearchFriendNeedEQ"), postfix: false);
        }

        private static void Patch(Harmony harmony, MethodBase target, bool postfix)
        {
            if (target == null)
                throw new MissingMethodException("关注人数修复补丁目标不存在");

            HarmonyMethod patch = new HarmonyMethod(
                typeof(RelationFocusCountPatches),
                postfix ? nameof(RelationFocusCountPatches.SyncPostfix) : nameof(RelationFocusCountPatches.SyncPrefix));
            harmony.Patch(target, prefix: postfix ? null : patch, postfix: postfix ? patch : null);
        }
    }

    internal static class RelationFocusCountPatches
    {
        internal static void SyncPrefix() => RelationFocusCountService.SyncCurrent();
        internal static void SyncPostfix() => RelationFocusCountService.SyncCurrent();
    }
}
