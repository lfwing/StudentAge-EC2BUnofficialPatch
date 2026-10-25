using System;
using System.Collections.Generic;
using System.Linq;
using Condition;
using EC2BUnofficialPatch.Core;
using HarmonyLib;
using Sdk;

namespace EC2BUnofficialPatch.Features.Effects
{
    internal sealed class ConditionExtensionModule : IPluginModule
    {
        public string Key => "conditions.extensions";
        public IReadOnlyList<ModuleLogItem> LogItems => new[] { new ModuleLogItem("前提", "新前提扩展") };
        public void Load(Harmony harmony, PluginServices services)
        {
            foreach (Type target in new[] { typeof(ConditionerRelation), typeof(ConditionerUI) })
            {
                EffectExtensionData.Patch(harmony, target, "OnIsMatch", typeof(ConditionExtensionPatches), prefix: nameof(ConditionExtensionPatches.Match));
                EffectExtensionData.Patch(harmony, target, "OnToString", typeof(ConditionExtensionPatches), prefix: nameof(ConditionExtensionPatches.Description));
                EffectExtensionData.Patch(harmony, target, "OnGetProgress", typeof(ConditionExtensionPatches), prefix: nameof(ConditionExtensionPatches.Progress));
            }
        }
    }

    internal static class ConditionExtensionPatches
    {
        private static readonly AccessTools.FieldRef<Conditioner, List<double>> Raw =
            AccessTools.FieldRefAccess<Conditioner, List<double>>("condition");
        private static readonly AccessTools.FieldRef<RoleMgr, RoleModel> Model =
            AccessTools.FieldRefAccess<RoleMgr, RoleModel>("model");

        private static bool Handles(List<double> c) => c != null && c.Count >= 2 &&
            ((c[0] == 7 && c[1] == 2) || (c[0] == 101 && c[1] == 100));

        internal static bool TryModId(double raw, out ulong id)
        {
            id = 0;
            // Every integer <= 2^53-1 is exact in the game's List<double> CFG.
            // Never use ConditionerUI.id: its constructor narrows this parameter to int.
            if (double.IsNaN(raw) || double.IsInfinity(raw) || raw <= 0 || raw > 9007199254740991d || raw != Math.Truncate(raw)) return false;
            id = (ulong)raw;
            return true;
        }

        internal static int FavorRank(int personId)
        {
            var model = Model(Singleton<RoleMgr>.Ins);
            if (model?.roleDict == null || !model.roleDict.TryGetValue(personId, out var target) ||
                personId == 0 || target == null || target.Relation <= 0) return 0;
            // Competition ranking: equal favor shares a rank (1,2,2,4).
            return 1 + model.roleDict.Values.Count(r => r != null && r.id != 0 && r.Relation > 0 && r.Favor > target.Favor);
        }

        internal static bool Evaluate(List<double> c)
        {
            if (!Handles(c)) return false;
            if (c[0] == 101)
                return c.Count == 3 && TryModId(c[2], out ulong id) &&
                    Singleton<ModCtrl>.Ins.activeMods != null && Singleton<ModCtrl>.Ins.activeMods.Contains(id);
            if (c.Count != 4 || c[2] <= 0 || c[2] > int.MaxValue || c[2] != Math.Truncate(c[2]) ||
                c[3] < 1 || c[3] > int.MaxValue || c[3] != Math.Truncate(c[3])) return false;
            return FavorRank((int)c[2]) == (int)c[3];
        }

        internal static bool Match(Conditioner __instance, ref bool __result)
        {
            var c = Raw(__instance);
            if (!Handles(c)) return true;
            __result = Evaluate(c);
            return false;
        }

        internal static bool Description(Conditioner __instance, int _type, ref string __result)
        {
            var c = Raw(__instance);
            if (!Handles(c)) return true;
            string text;
            if (c[0] == 101)
                text = c.Count == 3 && TryModId(c[2], out ulong id) ? "Mod " + id + "已启用" : "无效的Mod启用前提";
            else
                text = c.Count == 4 ? "角色" + c[2].ToString("0") + "是已结识角色中好感度第" + c[3].ToString("0") + "名" : "无效的好感排名前提";
            __result = HtmlTxtUtil.ToStr(text, Evaluate(c), _type, false);
            return false;
        }

        internal static bool Progress(Conditioner __instance, ref ValueTuple<float, float> __result)
        {
            var c = Raw(__instance);
            if (!Handles(c)) return true;
            __result = new ValueTuple<float, float>(Evaluate(c) ? 1 : 0, 1);
            return false;
        }
    }
}
