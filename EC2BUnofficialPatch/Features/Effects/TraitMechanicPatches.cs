using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Config;
using Effect;
using HarmonyLib;
using Increase;
using Sdk;
using TheEntity;

namespace EC2BUnofficialPatch.Features.Effects
{
    internal static class TraitMechanicPatches
    {
        internal const RoleIncType MechanicType = (RoleIncType)195053;
        internal const RoleIncType SocialHistoryType = (RoleIncType)195055;
        internal const int Marker = -1951524;
        private const int SocialHistoryMarker = -1951526;
        [ThreadStatic] private static int freeDepth;
        [ThreadStatic] private static bool socialEvent;
        [ThreadStatic] private static Stack<Preparation> preparing;
        [ThreadStatic] private static RewardContext rewardContext;
        internal static bool IsFreeAction => freeDepth > 0;
        private static readonly MethodInfo HelpAction = AccessTools.Method(typeof(ActionData), "HelpAction");

        internal static void Install(Harmony harmony)
        {
            var p = typeof(TraitMechanicPatches);
            EffectExtensionData.Patch(harmony, typeof(CommonEvtMgr), "GenEffector", p, prefix: nameof(Factory),
                arguments: new[] { typeof(List<float>), typeof(Effector), typeof(int), typeof(int) });
            EffectExtensionData.Patch(harmony, typeof(EffectorSpecial), "OnRun", p, prefix: nameof(Run));
            EffectExtensionData.Patch(harmony, typeof(EffectorSpecial), "OnToString", p, prefix: nameof(Description));
            EffectExtensionData.Patch(harmony, typeof(EffectorChangeAttr), "OnRun", p, prefix: nameof(AttributeReward), postfix: nameof(ExtraAttributeReward));
            EffectExtensionData.Patch(harmony, typeof(ActionData), "HelpAction", p, prefix: nameof(BeginAction),
                finalizer: nameof(EndAction), transpiler: nameof(WrapCompletion));
            EffectExtensionData.Patch(harmony, typeof(ActionData), "HelpLoveAction", p, prefix: nameof(BreakStreak));
            EffectExtensionData.Patch(harmony, typeof(ActionData), "Action", p, prefix: nameof(OtherAction),
                arguments: new[] { typeof(ActionSubData), typeof(bool), typeof(int), typeof(bool), typeof(int), typeof(bool) });
            EffectExtensionData.Patch(harmony, typeof(ActionData), "RunSocialEffect", p, postfix: nameof(SocialComplete));
            EffectExtensionData.Patch(harmony, typeof(ActionData), "HelpSocialEvt", p, transpiler: nameof(DeferSocialEvent));
        }

        private static bool Matches(List<float> e) => e != null && e.Count >= 2 && e[1] == 1524 && (e[0] == 40 || e[0] == 20);
        internal static bool Valid(List<float> e) => Matches(e) && e.Count >= 4 &&
            ((e[0] == 40 && (e[2] == 1 || e[2] == 2 || e[2] == 3) && e.Count == (e[2] == 3 ? 5 : 4) && e[3] >= 0 && e[3] <= 100) ||
             (e[0] == 20 && e[2] == 1 && e.Count == 5 && e[3] >= 0 && e[4] >= 0 && e[4] == Math.Truncate(e[4]))) &&
            e.All(EffectExtensionData.Finite) && (e.Count != 5 || e[4] >= 0);

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
            if (Valid(e))
                Singleton<RoleMgr>.Ins.GetRole(__instance.toRoleId)?.AddEffect(MechanicType, e[0] == 20 ? 4 : (int)e[2],
                    new IncreaserOther(null) { otherAttrId = Marker, id = e[0] == 20 ? 4 : (int)e[2],
                        value = e[0] == 20 ? e[3] : e[3] / 100, value2 = e.Count == 5 ? (e[0] == 20 ? e[4] : e[4] / 100) : 0,
                        format = e[0] == 20 ? __instance.fromRoleId + "|" + __instance.tag + "|" + string.Join(",", e) : null }, __instance);
            else EC2BUnofficialPatch.Core.PatchLog.Warning("新效果扩展-无效行动/社交参数：" + string.Join(",", e));
            return false;
        }
        internal static bool Description(EffectorSpecial __instance, ref string __result)
        {
            var e = EffectExtensionData.Get(__instance);
            if (!Matches(e)) return true;
            if (!Valid(e)) __result = "无效效果「" + string.Join(",", e) + "」（此项不生效；请核对模式、项数与非负数值，概率范围0～100）";
            else if (e[0] == 20) __result = "每回合首次与某位关注对象完成社交后，心情+" + EffectExtensionData.Number(e[3]) +
                "，每回合最多触发" + EffectExtensionData.Number(e[4]) + "次";
            else if (e[2] == 2) __result = "进行具有属性收益的行动时，有" + EffectExtensionData.Number(e[3]) + "%的概率视为额外无损行动一次";
            else __result = "连续进行同一项具有属性收益的行动时，有" + EffectExtensionData.Number(e[3]) + "%的概率" +
                (e[2] == 1 ? "获得双倍属性" : "额外消耗相当于精力恢复上限" + EffectExtensionData.Number(e[4]) + "%的精力，获得三倍属性");
            return false;
        }

        internal static bool Eligible(ActionCfg cfg) => cfg != null && cfg.type != 1 && cfg.type != 2 && cfg.type != 3 &&
            cfg.id != 3192 && cfg.funcId == 0 && cfg.evtId == 0 && cfg.minigameId == 0 && cfg.evtType == 0 &&
            cfg.effect != null && cfg.effect.Any(e => e.Count == 4 && e[0] == 1 && e[1] == 1 && e[3] > 0 && EffectExtensionData.Attribute(e[2]));
        private static IEnumerable<IncreaserOther> Nodes(int mode) => CostRewardPatches.Chain(
            Singleton<RoleMgr>.Ins.GetRole()?.IncCtrl.Get(MechanicType, mode)).OfType<IncreaserOther>();

        internal sealed class Preparation
        {
            internal ActionSubData Data;
            internal Dictionary<ulong, bool> Continuous = new Dictionary<ulong, bool>();
        }
        private sealed class RewardContext { internal HashSet<List<float>> Effects; internal float Multiple; internal bool Repeating; }

        internal static void BeginAction(ActionSubData _data)
        {
            var state = new Preparation { Data = _data };
            if (!IsFreeAction)
                foreach (var node in Nodes(1).Concat(Nodes(3)))
                {
                    state.Continuous[node.uid] = node.totalCnt > 0 && node.id2 == _data.id;
                    node.totalCnt = node.id2 == _data.id ? node.totalCnt + 1 : 1;
                    node.id2 = _data.id;
                }
            if (preparing == null) preparing = new Stack<Preparation>();
            preparing.Push(state);
        }
        internal static Exception EndAction(Exception __exception)
        {
            if (preparing?.Count > 0) preparing.Pop();
            return __exception;
        }
        internal static void BreakStreak()
        {
            if (!IsFreeAction) foreach (var node in Nodes(1).Concat(Nodes(3))) { node.totalCnt = 0; node.id2 = 0; }
        }
        internal static void OtherAction(ActionSubData _data)
        {
            if (_data != null && Cfg.ActionCfgMap.TryGetValue(_data.id, out var cfg) &&
                (cfg.type == 1 || cfg.type == 2 || cfg.id == 3192)) BreakStreak();
        }

        internal static Action Wrap(Action close, ActionData action, ActionSubData data)
        {
            var state = preparing.Peek();
            return IsFreeAction ? close : (Action)(() => Complete(close, action, data, state));
        }
        private static bool Roll(float fraction) => fraction >= 1 || (fraction > 0 && UnityEngine.Random.value < fraction);
        internal static void Complete(Action close, ActionData action, ActionSubData data, Preparation state)
        {
            var cfg = Cfg.ActionCfgMap[data.id];
            if (!Eligible(cfg)) { close(); return; }
            var role = Singleton<RoleMgr>.Ins.GetRole();
            float multiple = 1;
            foreach (var node in Nodes(1))
                if (state.Continuous.TryGetValue(node.uid, out bool continuous) && continuous && Roll(node.GetSelfValue())) multiple += 1;
            foreach (var node in Nodes(3))
                if (state.Continuous.TryGetValue(node.uid, out bool continuous) && continuous && Roll(node.GetSelfValue()))
                {
                    float cost = Math.Max(0, role.GetAttr(9)) * node.value2;
                    if (Singleton<RoleMgr>.Ins.Cost(7, cost, node.tag, false)) multiple += 2;
                }
            bool again = Nodes(2).Any(n => Roll(n.GetSelfValue()));
            var previous = rewardContext;
            rewardContext = new RewardContext { Effects = new HashSet<List<float>>(cfg.effect), Multiple = multiple };
            try { close(); }
            finally { rewardContext = previous; }
            if (again)
            {
                freeDepth++;
                try { HelpAction.Invoke(action, new object[] { data, null, 0, true }); }
                finally { freeDepth--; }
            }
        }

        internal static bool AttributeReward(EffectorChangeAttr __instance, ref float _rate)
        {
            var e = EffectExtensionData.Get(__instance);
            if (e == null || e.Count != 4 || e[0] != 1 || e[1] != 1) return true;
            if (IsFreeAction && e[3] < 0) return false;
            return true;
        }

        internal static void ExtraAttributeReward(EffectorChangeAttr __instance, float _rate, bool _toast)
        {
            var context = rewardContext;
            var e = EffectExtensionData.Get(__instance);
            if (context == null || context.Repeating || context.Multiple <= 1 || e == null || e.Count != 4 ||
                e[0] != 1 || e[1] != 1 || e[3] <= 0 || !context.Effects.Contains(e)) return;
            context.Repeating = true;
            try { for (int i = 1; i < context.Multiple; i++) __instance.OnRun(_rate, _toast); }
            finally { context.Repeating = false; }
        }

        internal static bool RunFreeClose(Action close)
        {
            if (!IsFreeAction) return false;
            close(); return true;
        }

        internal static IEnumerable<CodeInstruction> WrapCompletion(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var code = instructions.ToList();
            int make = code.FindIndex(i => i.opcode == OpCodes.Ldftn && i.operand is MethodInfo m && m.Name.Contains("<HelpAction>b__0"));
            int condition = code.FindLastIndex(i => i.Calls(AccessTools.Method(typeof(RoleMgr), "UpdateConditionData")));
            if (make < 0 || condition < 5 || code[make + 1].opcode != OpCodes.Newobj)
                throw new InvalidOperationException("行动扩展：无法定位原版行动结算回调。");
            var end = generator.DefineLabel();
            code[condition - 5].labels.Add(end);
            var store = code[make + 2];
            if (store.opcode != OpCodes.Stfld || !(store.operand is FieldInfo field) || field.FieldType != typeof(Action))
                throw new InvalidOperationException("行动扩展：无法定位回调字段。");
            code.InsertRange(make + 3, new[] { new CodeInstruction(code[make - 1].opcode, code[make - 1].operand),
                new CodeInstruction(OpCodes.Ldfld, field),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TraitMechanicPatches), nameof(RunFreeClose))), new CodeInstruction(OpCodes.Brtrue, end) });
            code.InsertRange(make + 2, new[] { new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TraitMechanicPatches), nameof(Wrap))) });
            return code;
        }

        internal static void SocialComplete(Role _npc)
        {
            if (socialEvent) return;
            BreakStreak();
            if (_npc == null || _npc.id == 0 || _npc.Relation <= 0) return;
            int round = Singleton<RoundMgr>.Ins.GetRound();
            var role = Singleton<RoleMgr>.Ins.GetRole();
            foreach (var node in Nodes(4).ToList())
            {
                if (node.GetSelfValue() <= 0) continue;
                // Round history is not owned by the active trait UID: switching
                // away/back or rebuilding Insight must not reset the round cap.
                var state = CostRewardPatches.Chain(role.IncCtrl.Get(SocialHistoryType, 0)).OfType<IncreaserOther>()
                    .FirstOrDefault(n => n.otherAttrId == SocialHistoryMarker && n.format == node.format);
                if (state == null)
                {
                    state = new IncreaserOther(null) { otherAttrId = SocialHistoryMarker, format = node.format };
                    role.AddEffect(SocialHistoryType, 0, state, null);
                }
                if (state.id2 != round) { state.id2 = round; state.totalCnt = 0; state.ids = new List<int>(); }
                if (state.ids == null) state.ids = new List<int>();
                if (state.ids.Contains(_npc.id)) continue;
                state.ids.Add(_npc.id);
                if (state.totalCnt >= node.value2) continue;
                state.totalCnt++;
                role.UpdateAttr(0, node.GetSelfValue(), 1, node.tag, 2);
            }
        }

        internal static void RunSocialEventEffect(ActionData action, Role npc)
        {
            bool previous = socialEvent; socialEvent = true;
            try { action.RunSocialEffect(npc); }
            finally { socialEvent = previous; }
        }
        internal static void ShowSocialEvent(CommonEvtMgr events, int id, float rate, Action callback, int bg, bool music, string url, int npcId)
        {
            bool complete = false;
            Action finish = () => {
                if (complete) return;
                complete = true; callback?.Invoke(); SocialComplete(Singleton<RoleMgr>.Ins.GetRole(npcId));
            };
            events.ShowEvent(id, rate, finish, bg, music, url);
            // Native effect-only events execute synchronously and omit _callback.
            if (Cfg.EvtCfgMap.TryGetValue(id, out var cfg) && cfg.type != 50 && cfg.type != 60 && cfg.displayType != 1 &&
                string.IsNullOrEmpty(cfg.content) && (cfg.talkId == null || cfg.talkId.Count == 0)) finish();
        }
        internal static IEnumerable<CodeInstruction> DeferSocialEvent(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            int run = code.FindIndex(i => i.Calls(AccessTools.Method(typeof(ActionData), "RunSocialEffect")));
            int show = code.FindIndex(i => i.Calls(AccessTools.Method(typeof(CommonEvtMgr), "ShowEvent")));
            if (run < 0 || show < 0) throw new InvalidOperationException("社交扩展：无法定位事件社交的完成回调。");
            code[run].opcode = OpCodes.Call;
            code[run].operand = AccessTools.Method(typeof(TraitMechanicPatches), nameof(RunSocialEventEffect));
            code[show].opcode = OpCodes.Call;
            code[show].operand = AccessTools.Method(typeof(TraitMechanicPatches), nameof(ShowSocialEvent));
            code.Insert(show, new CodeInstruction(OpCodes.Ldarg_1));
            return code;
        }
    }
}
