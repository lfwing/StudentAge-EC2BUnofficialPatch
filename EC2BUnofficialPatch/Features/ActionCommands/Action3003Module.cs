using System;
using System.Collections.Generic;
using System.Reflection;
using EC2BUnofficialPatch.Core;
using HarmonyLib;
using View.Evt;

namespace EC2BUnofficialPatch.Features.ActionCommands
{
    internal sealed class Action3003Module : IPluginModule
    {
        private static readonly MethodInfo PostfixMethod =
            AccessTools.Method(typeof(Action3003Module), nameof(RepairScaleResult));
        private static readonly IReadOnlyList<ModuleLogItem> Items = new[]
        {
            new ModuleLogItem("行动指令", "3003修复")
        };

        public string Key => "action.3003";

        public IReadOnlyList<ModuleLogItem> LogItems => Items;

        public void Load(Harmony harmony, PluginServices services)
        {
            PatchTalkView(harmony, typeof(NewTalkView));
            PatchTalkView(harmony, typeof(PreviewTalkView));
        }

        private static void PatchTalkView(Harmony harmony, Type talkViewType)
        {
            MethodInfo target = AccessTools.Method(talkViewType, "HelpCheckRoleAction")
                ?? throw new MissingMethodException(
                    talkViewType.FullName,
                    "HelpCheckRoleAction");
            harmony.Patch(target, postfix: new HarmonyMethod(PostfixMethod));
        }

        private static void RepairScaleResult(
            NewTalkRoleData role,
            int actionId,
            List<float> parms,
            ref ValueTuple<float, float, float> __result)
        {
            if (actionId == CommandIds.ScaleRole && parms != null && parms.Count > 0)
            {
                __result.Item3 = parms[0];
            }
        }
    }
}
