using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection.Emit;
using Config;
using EC2BUnofficialPatch.Core;
using Effect;
using HarmonyLib;
using Increase;
using Sdk;
using TheEntity;

namespace EC2BUnofficialPatch.Features.Effects
{
    internal sealed class NewEffectModule : IPluginModule
    {
        public string Key => "effects.extensions";
        public IReadOnlyList<ModuleLogItem> LogItems => new[] { new ModuleLogItem("效果", "新效果扩展") };
        public void Load(Harmony harmony, PluginServices services)
        {
            InstallRuntime(harmony);
            TraitSelectionPatches.Install(harmony);
        }

        internal static void InstallRuntime(Harmony harmony)
        {
            TraitCooldownPatches.Install(harmony);
            TraitMechanicPatches.Install(harmony);
            InsightEffectPatches.Install(harmony);
            CostRewardPatches.Install(harmony);
            Type p = typeof(NewEffectPatches);
            EffectExtensionData.Patch(harmony, typeof(CommonEvtMgr), "GenEffector", p, prefix: nameof(NewEffectPatches.Factory),
                arguments: new[] { typeof(List<float>), typeof(Effector), typeof(int), typeof(int) });
            EffectExtensionData.Patch(harmony, typeof(EffectorChangeAttr), "OnRun", p, prefix: nameof(NewEffectPatches.AttributeRun));
            EffectExtensionData.Patch(harmony, typeof(EffectorChangeAttr), "OnToString", p, prefix: nameof(NewEffectPatches.AttributeDescription));
            EffectExtensionData.Patch(harmony, typeof(EffectorSpecial), "OnRun", p, prefix: nameof(NewEffectPatches.SpecialRun));
            EffectExtensionData.Patch(harmony, typeof(EffectorSpecial), "OnToString", p, prefix: nameof(NewEffectPatches.SpecialDescription));
            EffectExtensionData.Patch(harmony, typeof(IncreaserOther), "OnGetSelfValue", p, prefix: nameof(NewEffectPatches.DynamicValue), postfix: nameof(NewEffectPatches.MoodValue));
            EffectExtensionData.Patch(harmony, typeof(IncreaserOther), "OnToString", p, prefix: nameof(NewEffectPatches.DynamicDescription));
            EffectExtensionData.Patch(harmony, typeof(Role), "GetRealAddValue", p, postfix: nameof(NewEffectPatches.ChangeValue));
            EffectExtensionData.Patch(harmony, typeof(Role), "UpdateAttr", p, prefix: nameof(NewEffectPatches.EnterUpdate),
                finalizer: nameof(NewEffectPatches.LeaveUpdate), transpiler: nameof(NewEffectPatches.FinalChange));
            EffectExtensionData.Patch(harmony, typeof(Role), "UpdateAttr2", p, prefix: nameof(NewEffectPatches.EnterUpdate2), finalizer: nameof(NewEffectPatches.LeaveUpdate));
            EffectExtensionData.Patch(harmony, typeof(Role), "SetAttr", p, prefix: nameof(NewEffectPatches.DirectSet), postfix: nameof(NewEffectPatches.AfterSet));
            EffectExtensionData.Patch(harmony, typeof(RoleMgr), "HasEnoughCost", p, prefix: nameof(NewEffectPatches.CostCheck),
                arguments: new[] { typeof(int), typeof(float), typeof(bool) });
        }
    }

    internal static class NewEffectPatches
    {
        // Original concrete increaser types remain MessagePack-serializable. These
        // private numeric buckets never enter native attribute/statistics categories.
        internal const RoleIncType CostReduction = (RoleIncType)195050;
        internal const RoleIncType MoodCoefficient = (RoleIncType)195051;
        internal const int FixedCostMarker = -1950501;
        internal const int AttributeRateMarker = -1950102;
        internal const int WorldviewCostMarker = -1950502;
        [ThreadStatic] private static Dictionary<Role, int> updateDepth;

        private static bool NewAttribute(List<float> e) => e != null && e.Count >= 2 && e[0] == 1 && e[1] == 1145;
        private static bool NewSpecial(List<float> e) => e != null && e.Count >= 2 && e[0] == 999 && e[1] == 1145;
        internal static bool ValidAttribute(List<float> e) => e.Count == 8 && e[2] == 1 && EffectExtensionData.Attribute(e[3]) &&
            e[4] > 0 && EffectExtensionData.Finite(e[4]) && EffectExtensionData.Attribute(e[5]) &&
            e[6] >= 0 && EffectExtensionData.Finite(e[6]) && e[7] >= 0 && EffectExtensionData.Finite(e[7]);
        internal static bool ValidSpecial(List<float> e) => SpecialError(e) == null;
        internal static bool Consumable(int id) => Cfg.PersonAttrCfgMap.TryGetValue(id, out var cfg) &&
            (cfg.consume == 1 || cfg.consume == 2);

        internal static string SpecialError(List<float> e)
        {
            if (e.Count < 3) return "缺少第3项效果模式";
            if (e[2] != 1 && e[2] != -1 && e[2] != 2 && e[2] != 3 && e[2] != 999)
                return "第3项是效果模式（1=固定减耗，-1=固定增耗，2=按强化/批判数量减耗，3=心情系数，999=减耗奖励），不是属性ID；模式1/-1/2的第4项才是personAttrId";
            bool scalar = e[2] == 3 || e[2] == 999;
            if (e.Count != (scalar ? 4 : 5)) return scalar
                ? "模式3/999要求4项：999,1145,效果模式,百分比"
                : "模式1/-1/2要求5项：999,1145,效果模式,personAttrId,百分比";
            if (!scalar && !EffectExtensionData.Attribute(e[3]))
                return "第4项personAttrId必须是PersonAttrCfg中存在的整数属性ID";
            if (!scalar && !Consumable((int)e[3]))
                return "第4项personAttrId必须是可消耗属性（PersonAttrCfg.consume为1或2）";
            float percent = e[scalar ? 3 : 4];
            if (!EffectExtensionData.Finite(percent) || percent < 0)
                return (scalar ? "第4项" : "第5项") + "百分比必须是有限非负数";
            return null;
        }

        internal static bool Factory(List<float> _effect, Effector _effector, int _toRoleId, int _fromRoleId, ref Effector __result)
        {
            if (!NewAttribute(_effect) && !NewSpecial(_effect)) return true;
            if (NewAttribute(_effect) ? ValidAttribute(_effect) : ValidSpecial(_effect)) return true;
            PatchLog.Warning("新效果扩展-无效参数不执行：" + string.Join(",", _effect) +
                (NewSpecial(_effect) ? "；" + SpecialError(_effect) : ""));
            // All malformed extensions retain a safe description node.
            __result = new EffectorSpecial(_effector, _effect) { toRoleId = _toRoleId, fromRoleId = _fromRoleId };
            return false;
        }

        internal static bool AttributeRun(EffectorChangeAttr __instance)
        {
            var e = EffectExtensionData.Get(__instance);
            if (!NewAttribute(e)) return true;
            if (ValidAttribute(e))
            {
                var role = Singleton<RoleMgr>.Ins.GetRole(__instance.toRoleId);
                if (role != null) role.AddEffect(RoleIncType.AttrMul, (int)e[5], new IncreaserOther(null) {
                    otherAttrId = AttributeRateMarker, id = (int)e[3], format = e[4].ToString("R", CultureInfo.InvariantCulture), id2 = (int)e[5],
                    value2 = e[6] / 100, value3 = e[7] / 100 }, __instance);
            }
            else PatchLog.Warning("新效果扩展-1,1145,1 参数无效，要求属性ID、正数步长、非负百分比和上限。");
            return false;
        }

        internal static bool AttributeDescription(EffectorChangeAttr __instance, int _type, ref string __result)
        {
            var e = EffectExtensionData.Get(__instance);
            if (!NewAttribute(e)) return true;
            __result = ValidAttribute(e) ? AttributeText((int)e[3], e[4], (int)e[5], e[6] / 100, e[7] / 100, _type) : "无效的1,1145,1扩展效果";
            return false;
        }

        internal static string AttributeText(int source, float step, int target, float rate, float cap, int type) =>
            "每" + EffectExtensionData.Number(step) + "点" + EffectExtensionData.Attr(source, type) + "增加" +
            EffectExtensionData.Number(rate * 100) + "%的" + EffectExtensionData.Attr(target, type) + "获取率，最高" +
            EffectExtensionData.Number(cap * 100) + "%";

        internal static bool SpecialRun(EffectorSpecial __instance)
        {
            var e = EffectExtensionData.Get(__instance);
            if (NewAttribute(e)) return false;
            if (!NewSpecial(e)) return true;
            if (!ValidSpecial(e))
            {
                PatchLog.Warning("新效果扩展-999,1145 参数无效：" + SpecialError(e));
                return false;
            }
            var role = Singleton<RoleMgr>.Ins.GetRole(__instance.toRoleId);
            if (role == null) return false;
            if (e[2] == 999)
                role.AddEffect(CostRewardPatches.RewardType, 0, new IncreaserOther(null) {
                    otherAttrId = CostRewardPatches.RewardMarker, value = e[3] / 100, format = CostRewardPatches.Group }, __instance);
            else if (e[2] == 1 || e[2] == -1)
                role.AddEffect(CostReduction, (int)e[3], new IncreaserOther(null) { otherAttrId = FixedCostMarker,
                    id = (int)e[3], value = (e[2] == 1 ? 1 : -1) * e[4] / 100, format = CostRewardPatches.Group }, __instance);
            else if (e[2] == 2)
                role.AddEffect(CostReduction, (int)e[3], new IncreaserOther(null) { otherAttrId = WorldviewCostMarker, id = (int)e[3], value2 = e[4] / 100, format = CostRewardPatches.Group }, __instance);
            else
                role.AddEffect(MoodCoefficient, 0, new IncreaserAttr(null) { attrId = 0, value = e[3] / 100 }, __instance);
            return false;
        }

        internal static bool SpecialDescription(EffectorSpecial __instance, int _type, ref string __result)
        {
            var e = EffectExtensionData.Get(__instance);
            if (NewAttribute(e)) { __result = "无效效果：要求1,1145,1,来源属性,点数,目标属性,百分比,上限（此项不生效）"; return false; }
            if (!NewSpecial(e)) return true;
            if (!ValidSpecial(e)) __result = "无效效果「" + string.Join(",", e.Select(EffectExtensionData.Number)) +
                "」：" + SpecialError(e) + "（此项不生效）";
            else if (e[2] == 3) __result = "心情带来的三维获取率加成系数额外提升" + EffectExtensionData.Number(e[3]) + "%";
            else if (e[2] == 999) __result = "每次减少某属性消耗后，获得相当于实际减免量" +
                EffectExtensionData.Number(e[3]) + "%值的随机一项指定属性";
            else __result = (e[2] == 2 ? "每个强化/批判的方法论/价值观，" : "") +
                (e[2] == -1 ? "增加" : "减少") + EffectExtensionData.Number(e[4]) + "%的" + EffectExtensionData.Attr((int)e[3], _type) + "消耗";
            return false;
        }

        internal static int ModifiedWorldviewCount()
        {
            var data = Singleton<RoleMgr>.Ins.GetValueviewData();
            int worldviews = data?.groups?.Values.Where(g => g?.nodes != null).SelectMany(g => g.nodes)
                .Where(n => n != null && n.state == ValueviewNodeState.Enable && (n.mode == 1 || n.mode == 2))
                .Select(n => n.srcId).Distinct().Count() ?? 0;
            var study = Singleton<RoleMgr>.Ins.GetStudyData(false);
            var methods = study == null ? null : AccessTools.Field(typeof(StudyData), "methods").GetValue(study) as List<MethodData>;
            return worldviews + (methods?.Where(m => m != null && (m.mode == 1 || m.mode == 2)).Select(m => m.srcId).Distinct().Count() ?? 0);
        }

        internal static bool DynamicValue(IncreaserOther __instance, ref float __result)
        {
            if (__instance.otherAttrId == FixedCostMarker || __instance.otherAttrId == CostRewardPatches.RewardMarker ||
                __instance.otherAttrId == TraitMechanicPatches.Marker || __instance.otherAttrId == InsightEffectPatches.Marker)
            { __result = __instance.value; return false; }
            if (__instance.otherAttrId == AttributeRateMarker)
            {
                var role = Singleton<RoleMgr>.Ins.GetRole(__instance.toRoleId);
                double source = role?.GetAttr(__instance.id, false) ?? 0;
                __result = float.TryParse(__instance.format, NumberStyles.Float, CultureInfo.InvariantCulture, out float step) && step > 0 && EffectExtensionData.Finite(step)
                    ? (float)Math.Min(Math.Max(0, __instance.value3), Math.Max(0, Math.Floor(source / step) * __instance.value2)) : 0;
                return false;
            }
            if (__instance.otherAttrId == WorldviewCostMarker)
            {
                __result = ModifiedWorldviewCount() * __instance.value2;
                return false;
            }
            return true;
        }

        internal static void MoodValue(IncreaserOther __instance, ref float __result)
        {
            if (__instance.otherAttrId != 605 || __result == 0) return;
            var role = Singleton<RoleMgr>.Ins.GetRole(__instance.toRoleId);
            float extra = Math.Max(0, role?.IncCtrl.GetValue(MoodCoefficient, 0) ?? 0);
            __result *= __result > 0 ? 1 + extra : Math.Max(0, 1 - extra);
        }

        internal static bool DynamicDescription(IncreaserOther __instance, int _type, ref string __result)
        {
            if (__instance.otherAttrId == AttributeRateMarker)
            {
                float.TryParse(__instance.format, NumberStyles.Float, CultureInfo.InvariantCulture, out float step);
                __result = AttributeText(__instance.id, step, __instance.id2, __instance.value2, __instance.value3, _type);
            }
            else if (__instance.otherAttrId == WorldviewCostMarker)
                __result = "每个强化/批判的方法论/价值观，减少" + EffectExtensionData.Number(__instance.value2 * 100) +
                    "%的" + EffectExtensionData.Attr(__instance.id, _type) + "消耗";
            else if (__instance.otherAttrId == FixedCostMarker)
                __result = (__instance.value >= 0 ? "减少" : "增加") + EffectExtensionData.Number(Math.Abs(__instance.value) * 100) + "%的" + EffectExtensionData.Attr(__instance.id, _type) + "消耗";
            else if (__instance.otherAttrId == CostRewardPatches.RewardMarker || __instance.otherAttrId == TraitMechanicPatches.Marker || __instance.otherAttrId == InsightEffectPatches.Marker)
                __result = __instance.tag;
            else return true;
            return false;
        }

        internal static float Reduction(Role role, int attribute)
        {
            if (role == null || !Consumable(attribute)) return 0;
            float sum = role.IncCtrl.GetValue(CostReduction, attribute);
            return EffectExtensionData.Finite(sum) ? Math.Min(1, sum) : (sum > 0 ? 1 : 0);
        }

        internal static void ChangeValue(Role __instance, int attrId, ref float __result)
        {
            if (updateDepth == null || !updateDepth.ContainsKey(__instance))
                __result = ReduceDelta(__instance, attrId, __result);
        }

        internal static float ReduceDelta(Role role, int attribute, float delta) => delta < 0 ?
            (TraitMechanicPatches.IsFreeAction ? 0 : delta * (1 - Reduction(role, attribute))) : delta;

        internal static float ApplyCost(Role role, int attribute, float delta)
        {
            CostRewardPatches.Capture(role, attribute, delta);
            return ReduceDelta(role, attribute, delta);
        }

        internal static IEnumerable<CodeInstruction> FinalChange(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            int calculate = code.FindIndex(i => i.Calls(AccessTools.Method(typeof(Role), "GetRealAddValue")));
            int insert = -1;
            for (int index = Math.Max(0, calculate + 2); index + 1 < code.Count; index++)
                if (code[index].opcode == OpCodes.Ldarg_1 && code[index + 1].opcode == OpCodes.Ldc_I4_8)
                { insert = index; break; }
            if (calculate < 0 || insert < 0) throw new InvalidOperationException("属性消耗减免：无法定位最终增量结算位置。");
            var store = new CodeInstruction(code[calculate + 1]);
            OpCode load;
            if (store.opcode == OpCodes.Stloc_0) load = OpCodes.Ldloc_0;
            else if (store.opcode == OpCodes.Stloc_1) load = OpCodes.Ldloc_1;
            else if (store.opcode == OpCodes.Stloc_2) load = OpCodes.Ldloc_2;
            else if (store.opcode == OpCodes.Stloc_3) load = OpCodes.Ldloc_3;
            else if (store.opcode == OpCodes.Stloc || store.opcode == OpCodes.Stloc_S) load = OpCodes.Ldloc;
            else throw new InvalidOperationException("属性消耗减免：无法识别属性增量局部变量。");
            var first = new CodeInstruction(OpCodes.Ldarg_0);
            first.labels.AddRange(code[insert].labels); code[insert].labels.Clear();
            // Apply after native AttrAddInc, before writes/records/linked effects. This
            // discounts the final negative amount and avoids double-discounting SetAttr.
            code.InsertRange(insert, new[] { first, new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(load, store.operand), new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NewEffectPatches), nameof(ApplyCost))),
                new CodeInstruction(store.opcode, store.operand) });
            return code;
        }

        internal static void EnterUpdate(Role __instance)
        {
            if (updateDepth == null) updateDepth = new Dictionary<Role, int>();
            updateDepth.TryGetValue(__instance, out int count);
            updateDepth[__instance] = count + 1;
            CostRewardPatches.Enter(__instance);
        }

        internal static void EnterUpdate2(Role __instance, int _attrId, ref float _addValue)
        {
            EnterUpdate(__instance);
            _addValue = ApplyCost(__instance, _attrId, _addValue);
        }

        internal static Exception LeaveUpdate(Role __instance, Exception __exception)
        {
            if (updateDepth != null && updateDepth.TryGetValue(__instance, out int count))
            {
                if (count <= 1) updateDepth.Remove(__instance);
                else updateDepth[__instance] = count - 1;
            }
            CostRewardPatches.Leave(__instance);
            return __exception;
        }

        internal static void DirectSet(Role __instance, int key, ref float value, out CostRewardPatches.Settlement __state)
        {
            float old = __instance.GetAttr(key, true);
            bool updating = updateDepth != null && updateDepth.ContainsKey(__instance);
            __state = CostRewardPatches.BeforeSet(__instance, key, old, value, updating);
            if (!updating && value < old) value = old + ReduceDelta(__instance, key, value - old);
            if (TraitMechanicPatches.IsFreeAction && value < old) value = old;
        }

        internal static void AfterSet(Role __instance, int key, CostRewardPatches.Settlement __state) =>
            CostRewardPatches.AfterSet(__instance, key, __state);

        internal static void CostCheck(RoleMgr __instance, int _id, ref float _cost)
        {
            if (TraitMechanicPatches.IsFreeAction) _cost = 0;
            else if (_cost > 0) _cost *= 1 - Reduction(__instance.GetRole(), _id);
        }
    }
}
