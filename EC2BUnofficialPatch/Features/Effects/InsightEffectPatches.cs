using System;
using System.Collections.Generic;
using System.Linq;
using Config;
using EC2BUnofficialPatch.Core;
using Effect;
using HarmonyLib;
using Increase;
using Newtonsoft.Json;
using Sdk;
using TheEntity;

namespace EC2BUnofficialPatch.Features.Effects
{
    internal sealed class DisabledInsightCleanupModule : IPluginModule
    {
        public string Key => "effects.extensions.cleanup";
        public IReadOnlyList<ModuleLogItem> LogItems => Array.Empty<ModuleLogItem>();
        public void Load(Harmony harmony, PluginServices services) => EffectExtensionData.Patch(harmony,
            typeof(RoleMgr), "Load", typeof(InsightEffectPatches), postfix: nameof(InsightEffectPatches.CleanupBorrowed));
    }

    internal static class InsightEffectPatches
    {
        internal const RoleIncType InsightType = (RoleIncType)195054;
        internal const int Marker = -1951525;
        [ThreadStatic] private static bool refreshing;
        internal sealed class State
        {
            public string Signature;
            public List<ulong> Uids = new List<ulong>();
        }

        internal static void Install(Harmony harmony)
        {
            var p = typeof(InsightEffectPatches);
            EffectExtensionData.Patch(harmony, typeof(CommonEvtMgr), "GenEffector", p, prefix: nameof(Factory),
                arguments: new[] { typeof(List<float>), typeof(Effector), typeof(int), typeof(int) });
            EffectExtensionData.Patch(harmony, typeof(EffectorSpecial), "OnRun", p, prefix: nameof(Run));
            EffectExtensionData.Patch(harmony, typeof(EffectorSpecial), "OnToString", p, prefix: nameof(Description));
            EffectExtensionData.Patch(harmony, typeof(RoleIncreaserCtrl), "Get", p, prefix: nameof(Refresh));
            EffectExtensionData.Patch(harmony, typeof(RoleIncreaserCtrl), "GetValue", p, prefix: nameof(Refresh));
            EffectExtensionData.Patch(harmony, typeof(Role), "GetAttrMulValue", p, prefix: nameof(RefreshRole));
            EffectExtensionData.Patch(harmony, typeof(BaseIncreaser), "OnRemove", p, postfix: nameof(Remove));
        }
        private static bool Matches(List<float> e) => e != null && e.Count >= 2 && e[0] == 999 && e[1] == 1524;
        private static bool Valid(List<float> e) => e.Count == 4 && e[2] == 1 && EffectExtensionData.Finite(e[3]) &&
            e[3] > 0 && e[3] == Math.Truncate(e[3]) && e[3] < int.MaxValue;
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
            if (Valid(e)) Singleton<RoleMgr>.Ins.GetRole(__instance.toRoleId)?.AddEffect(InsightType, 0,
                new IncreaserOther(null) { otherAttrId = Marker, id = (int)e[3], value = 1 }, __instance);
            else EC2BUnofficialPatch.Core.PatchLog.Warning("新效果扩展-无效备选特质参数：" + string.Join(",", e));
            return false;
        }
        internal static bool Description(EffectorSpecial __instance, ref string __result)
        {
            var e = EffectExtensionData.Get(__instance);
            if (!Matches(e)) return true;
            __result = Valid(e) ? "好感度排名第" + EffectExtensionData.Number(e[3]) + "的关注对象，其已解锁的挚友备选特质同时生效" :
                "无效效果：要求999,1524,1,正整数名次（此项不生效）";
            return false;
        }

        internal static Role Target(int rank)
        {
            var roles = Singleton<RoleMgr>.Ins.GetRoleDict().Values.Where(r => r != null && r.id != 0 && r.Relation > 0)
                .OrderByDescending(r => r.Favor).ThenBy(r => r.id).ToList();
            return roles.FirstOrDefault(r => 1 + roles.Count(other => other.Favor > r.Favor) == rank);
        }
        // Get/GetValue are small enough for the runtime to inline into native
        // attribute calculations before a patch is installed. Refresh at that
        // calculation boundary as well, before its first multiplier is read.
        internal static void RefreshRole(Role __instance) => Refresh(__instance.IncCtrl);
        internal static void Refresh(RoleIncreaserCtrl __instance)
        {
            if (refreshing) return;
            var head = __instance.GetModel(InsightType)?.Get(0);
            if (head == null) return;
            refreshing = true;
            try
            {
                var claimed = new HashSet<string>();
                foreach (var node in CostRewardPatches.Chain(head).OfType<IncreaserOther>().ToList())
                {
                    if (node.otherAttrId != Marker) continue;
                    var role = Singleton<RoleMgr>.Ins.GetRole(node.toRoleId);
                    if (role == null || role.IncCtrl != __instance) continue;
                    Role target = node.GetSelfValue() > 0 ? Target(node.id) : null;
                    int[] traits = target != null && target.Relation >= Singleton<RoleMgr>.Ins.GetTraitNeedRelation()
                        ? (target.Traits ?? new List<int>()).Take(target.Traits?.Count >= 4 ? 4 : 3).Distinct()
                            .Where(id => id != target.Trait && id != target.Trait2 && Cfg.TraitsCfgMap.ContainsKey(id) && claimed.Add(target.id + ":" + id)).ToArray()
                        : new int[0];
                    string signature = (target?.id ?? 0) + ":" + string.Join(",", traits);
                    State state = string.IsNullOrEmpty(node.format) ? new State() : JsonConvert.DeserializeObject<State>(node.format);
                    if (state.Signature == signature) continue;
                    role.RemoveEffect(state.Uids);
                    state = new State { Signature = signature };
                    // Persist each UID immediately. Save/load and controller removal
                    // can then withdraw precisely these borrowed effects.
                    node.format = JsonConvert.SerializeObject(state);
                    foreach (int trait in traits)
                    {
                        var effects = Cfg.TraitsCfgMap[trait].effect.Where(e => !Matches(e)).ToList();
                        Effector effect = CommonEvtMgr.GenEffector(effects, null, node.toRoleId, target.id);
                        if (effect == null) continue;
                        effect.SetIsInc(true);
                        effect.SetTag(target.Name + "·" + Cfg.TraitsCfgMap[trait].name);
                        effect.SetRateType(10001);
                        effect.Run();
                        state.Uids.AddRange(effect.GetBaseIncreaserUids());
                        node.format = JsonConvert.SerializeObject(state);
                    }
                }
            }
            finally { refreshing = false; }
        }

        internal static void Remove(BaseIncreaser __instance)
        {
            if (!(__instance is IncreaserOther node) || node.otherAttrId != Marker || string.IsNullOrEmpty(node.format)) return;
            var state = JsonConvert.DeserializeObject<State>(node.format);
            node.format = null;
            bool previous = refreshing; refreshing = true;
            try { Singleton<RoleMgr>.Ins.GetRole(node.toRoleId)?.RemoveEffect(state.Uids); }
            finally { refreshing = previous; }
        }

        internal static void CleanupBorrowed(RoleMgr __instance)
        {
            foreach (var role in __instance.GetRoleDict().Values)
                foreach (var node in CostRewardPatches.Chain(role.IncCtrl.GetModel(InsightType)?.Get(0)).ToList()) Remove(node);
        }
    }
}
