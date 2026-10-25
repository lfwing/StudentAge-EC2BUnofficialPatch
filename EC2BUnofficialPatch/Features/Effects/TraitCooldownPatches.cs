using System.Collections.Generic;
using Effect;
using GenUI.Common;
using GenUI.Main;
using HarmonyLib;
using Increase;
using Sdk;
using TheEntity;
using View.Common;
using View.Main;

namespace EC2BUnofficialPatch.Features.Effects
{
    internal static class TraitCooldownPatches
    {
        internal const RoleIncType NoCooldown = (RoleIncType)195056;
        internal const string Text = "特质卡片可随时切换，无冷却限制";

        internal static void Install(Harmony harmony)
        {
            var p = typeof(TraitCooldownPatches);
            EffectExtensionData.Patch(harmony, typeof(CommonEvtMgr), "GenEffector", p, prefix: nameof(Factory),
                arguments: new[] { typeof(List<float>), typeof(Effector), typeof(int), typeof(int) });
            EffectExtensionData.Patch(harmony, typeof(EffectorSpecial), "OnRun", p, prefix: nameof(Run));
            EffectExtensionData.Patch(harmony, typeof(EffectorSpecial), "OnToString", p, prefix: nameof(Description));
            EffectExtensionData.Patch(harmony, typeof(DetailSocialView), "OnRenderRelation", p, postfix: nameof(Render));
            EffectExtensionData.Patch(harmony, typeof(ActionDescHelper), "TraitChange", p, postfix: nameof(DescribeCooldown));
            EffectExtensionData.Patch(harmony, typeof(IncreaserAttr), "OnToString", p, prefix: nameof(NodeDescription));
        }

        private static bool Matches(List<float> e) => e != null && e.Count >= 2 && e[0] == 20 && e[1] == 5418;
        private static bool Valid(List<float> e) => e.Count == 4 && e[2] == 1 && e[3] == 1;
        internal static bool Factory(List<float> _effect, Effector _effector, int _toRoleId, int _fromRoleId, ref Effector __result)
        {
            if (!Matches(_effect)) return true;
            __result = new EffectorSpecial(_effector, _effect) { toRoleId = _toRoleId, fromRoleId = _fromRoleId };
            return false;
        }
        internal static bool Run(EffectorSpecial __instance)
        {
            var e = EffectExtensionData.Get(__instance);
            if (!Matches(e)) return true;
            if (Valid(e)) Singleton<RoleMgr>.Ins.GetRole(__instance.toRoleId)?.AddEffect(NoCooldown, 0,
                new IncreaserAttr(null) { isOtherAttr = true, attrId = -1955418, value = 1 }, __instance);
            else Core.PatchLog.Warning("新效果扩展-特质切换效果要求20,5418,1,1。");
            return false;
        }
        internal static bool Description(EffectorSpecial __instance, ref string __result)
        {
            var e = EffectExtensionData.Get(__instance);
            if (!Matches(e)) return true;
            __result = Valid(e) ? Text : "无效效果：要求20,5418,1,1（此项不生效）";
            return false;
        }
        internal static bool Enabled() => Singleton<RoleMgr>.Ins.GetRole()?.IncCtrl.GetValue(NoCooldown, 0) > 0;
        internal static bool NodeDescription(IncreaserAttr __instance, ref string __result)
        {
            if (__instance.attrId != -1955418) return true;
            __result = __instance.GetSelfValue() > 0 ? Text : null;
            return false;
        }
        internal static void Render(UICell _cell)
        {
            // Keep the native relationship gate and candidate count. Only lift the timer.
            if (_cell is Cell_RelationTitleItemUI cell && Enabled()) cell.btn_trait.interactable = true;
        }
        internal static void DescribeCooldown(int _type, ref DescData? __result)
        {
            if (_type == 0 && Enabled()) __result = new DescData { code = 1000, title = Text };
        }
    }
}
