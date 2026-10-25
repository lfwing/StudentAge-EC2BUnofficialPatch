using System;
using System.Collections.Generic;
using System.Globalization;
using Config;
using EC2BUnofficialPatch.Core;
using Effect;
using HarmonyLib;
using Increase;
using Sdk;
using TheEntity;

namespace EC2BUnofficialPatch.Features.Effects
{
    internal static class EffectExtensionData
    {
        private static readonly AccessTools.FieldRef<Effector, List<float>> Raw =
            AccessTools.FieldRefAccess<Effector, List<float>>("effect");

        internal static List<float> Get(Effector effect) => Raw(effect);
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static bool Attribute(float value) => Finite(value) && value == Math.Truncate(value) &&
            value >= 0 && value <= int.MaxValue && Cfg.PersonAttrCfgMap.ContainsKey((int)value);
        internal static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        internal static string Attr(int id, int type) => HtmlTxtUtil.ToAttrStr(id, type);
        internal static string Percent(float fraction, int type) => HtmlTxtUtil.ToStr(fraction, "{0:0.###%}", type, false);

        internal static void Patch(Harmony harmony, Type target, string method, Type patch,
            string prefix = null, string postfix = null, string finalizer = null, Type[] arguments = null, string transpiler = null)
        {
            var original = arguments == null ? AccessTools.Method(target, method) : AccessTools.Method(target, method, arguments);
            if (original == null) throw new MissingMethodException(target.FullName, method);
            harmony.Patch(original,
                prefix: prefix == null ? null : new HarmonyMethod(patch, prefix),
                postfix: postfix == null ? null : new HarmonyMethod(patch, postfix),
                transpiler: transpiler == null ? null : new HarmonyMethod(patch, transpiler),
                finalizer: finalizer == null ? null : new HarmonyMethod(patch, finalizer),
                ilmanipulator: null);
        }
    }

    internal sealed class AttributeEffectModule : IPluginModule
    {
        public string Key => "effects.attribute";
        public IReadOnlyList<ModuleLogItem> LogItems => new[] { new ModuleLogItem("效果", "1属性改变修复与扩展") };
        public void Load(Harmony harmony, PluginServices services)
        {
            EffectExtensionData.Patch(harmony, typeof(CommonEvtMgr), "GenEffector", typeof(AttributeEffectPatches), prefix: nameof(AttributeEffectPatches.Factory),
                arguments: new[] { typeof(List<float>), typeof(Effector), typeof(int), typeof(int) });
            EffectExtensionData.Patch(harmony, typeof(EffectorChangeAttr), "OnToString", typeof(AttributeEffectPatches), prefix: nameof(AttributeEffectPatches.Description));
            EffectExtensionData.Patch(harmony, typeof(IncreaserAttrPerAttr2), "OnRun", typeof(AttributeEffectPatches), prefix: nameof(AttributeEffectPatches.NativeLinkedRun));
            EffectExtensionData.Patch(harmony, typeof(Role), "SetAttr", typeof(AttributeEffectPatches), prefix: nameof(AttributeEffectPatches.BeforeSet), postfix: nameof(AttributeEffectPatches.AfterSet));
        }
    }

    internal static class AttributeEffectPatches
    {
        [ThreadStatic] private static HashSet<Tuple<Role, int>> updating;

        internal static bool Factory(List<float> _effect, Effector _effector, ref Effector __result)
        {
            if (_effect == null || _effect.Count < 2 || _effect[0] != 1 || (_effect[1] != 97 && _effect[1] != 1001)) return true;
            bool valid = _effect.Count == 6 && (_effect[1] == 1001 ? ValidLinked(_effect) :
                EffectExtensionData.Attribute(_effect[2]) && EffectExtensionData.Attribute(_effect[4]) &&
                EffectExtensionData.Finite(_effect[3]) && EffectExtensionData.Finite(_effect[5]));
            if (valid) return true;
            PatchLog.Warning("1属性改变修复与扩展-无效参数已跳过：" + string.Join(",", _effect));
            __result = _effector;
            return false;
        }

        internal static bool Description(EffectorChangeAttr __instance, float _rate, int _type, ref string __result)
        {
            var e = EffectExtensionData.Get(__instance);
            if (e == null || e.Count != 6 || e[0] != 1 || !EffectExtensionData.Finite(_rate)) return true;
            if (e[1] == 97 && EffectExtensionData.Attribute(e[2]) && EffectExtensionData.Attribute(e[4]) &&
                EffectExtensionData.Finite(e[3]) && EffectExtensionData.Finite(e[5]))
            {
                __result = DescCtrl.GetTxt<string>(3027, new[] {
                    Cfg.PersonAttrCfgMap[(int)e[2]].Name(0, _type), EffectExtensionData.Number(e[3]),
                    EffectExtensionData.Attr((int)e[4], _type) + "获取率" + EffectExtensionData.Percent(e[5] * _rate, _type) });
                return false;
            }
            if (e[1] == 1001 && ValidLinked(e))
            {
                __result = "每当提升" + EffectExtensionData.Number(e[2]) + "点" + EffectExtensionData.Attr((int)e[3], _type) +
                    "时，" + HtmlTxtUtil.ToAttrStr((int)e[5], e[4], false, null, _type);
                return false;
            }
            return true;
        }

        private static bool ValidLinked(List<float> e) => e[2] > 0 && EffectExtensionData.Finite(e[2]) &&
            EffectExtensionData.Attribute(e[3]) && EffectExtensionData.Finite(e[4]) && EffectExtensionData.Attribute(e[5]);

        // Native 1001 ignores its threshold and only receives UpdateAttr's requested delta.
        // Notify once from SetAttr instead: UpdateAttr2/direct SetAttr and clamping then agree.
        internal static bool NativeLinkedRun(IncreaserAttrPerAttr2 __instance) => __instance.otherAttrId != 0;

        internal static void BeforeSet(Role __instance, int key, out float __state)
        {
            __state = Cfg.PersonAttrCfgMap.ContainsKey(key) ? __instance.GetAttr(key, true) : 0;
        }

        internal static void AfterSet(Role __instance, int key, float __state, float __result)
        {
            float delta = __result - __state;
            if (delta <= 0 || !EffectExtensionData.Finite(delta)) return;
            if (updating == null) updating = new HashSet<Tuple<Role, int>>();
            var guard = Tuple.Create(__instance, key);
            if (!updating.Add(guard)) return;
            try
            {
                for (BaseIncreaser node = __instance.IncCtrl.Get(RoleIncType.AttrPerAttr2, key); node != null;)
                {
                    BaseIncreaser next = node.parent;
                    if (node is IncreaserAttrPerAttr2 linked && linked.otherAttrId == 0 && linked.value > 0 &&
                        EffectExtensionData.Finite(linked.value) && EffectExtensionData.Finite(linked.value2) &&
                        Cfg.PersonAttrCfgMap.ContainsKey(linked.attrId2))
                    {
                        // max is unused by native 1001. Keep its fractional progress in the
                        // existing serialized field, so increments across rounds/load accumulate.
                        double progress = Math.Max(0, linked.max) + (double)delta;
                        double times = Math.Floor((progress + linked.value * 1e-7) / linked.value);
                        linked.max = (float)Math.Max(0, progress - times * linked.value);
                        float gain = (float)(times * linked.value2);
                        var target = Singleton<RoleMgr>.Ins.GetRole(linked.toRoleId);
                        if (gain != 0 && EffectExtensionData.Finite(gain) && target != null &&
                            !updating.Contains(Tuple.Create(target, linked.attrId2)))
                            target.UpdateAttr(linked.attrId2, gain, 1, linked.tag, 2);
                    }
                    node = next;
                }
            }
            finally { updating.Remove(guard); }
        }
    }
}
