using System;
using System.Collections.Generic;
using System.Linq;
using Effect;
using HarmonyLib;
using Increase;
using Sdk;
using TheEntity;

namespace EC2BUnofficialPatch.Features.Effects
{
    internal static class CostRewardPatches
    {
        internal const RoleIncType RewardType = (RoleIncType)195052;
        internal const int RewardMarker = -1950599;
        internal static readonly int[] Attributes = { 0, 1, 2, 3, 4, 7, 11, 30 };
        [ThreadStatic] private static int rewarding;
        [ThreadStatic] private static Stack<Tuple<Effector, string>> effects;
        private static readonly AccessTools.FieldRef<Effector, Effector> Decorator = AccessTools.FieldRefAccess<Effector, Effector>("effector");
        [ThreadStatic] private static Dictionary<Role, Stack<Frame>> frames;
        internal static string Group => effects?.Count > 0 ? effects.Peek().Item2 : null;

        internal static void Install(Harmony harmony)
        {
            EffectExtensionData.Patch(harmony, typeof(Effector), "Run", typeof(CostRewardPatches),
                prefix: nameof(BeginEffect), finalizer: nameof(EndEffect));
        }

        internal static void BeginEffect(Effector __instance)
        {
            if (effects == null) effects = new Stack<Tuple<Effector, string>>();
            string group = effects.Count > 0 && Decorator(effects.Peek().Item1) == __instance
                ? effects.Peek().Item2 : Singleton<ProfileMgr>.Ins.GenUid().ToString();
            effects.Push(Tuple.Create(__instance, group));
        }
        internal static Exception EndEffect(Exception __exception)
        {
            effects.Pop();
            return __exception;
        }

        internal sealed class Frame { internal int Attribute = -1; internal float Delta; internal bool Used; }
        internal sealed class Settlement { internal float Old, Cost, Sum; internal List<Tuple<float, float, string>> Rewards; }

        internal static void Enter(Role role)
        {
            if (frames == null) frames = new Dictionary<Role, Stack<Frame>>();
            if (!frames.TryGetValue(role, out var stack)) frames[role] = stack = new Stack<Frame>();
            stack.Push(new Frame());
        }
        internal static void Leave(Role role)
        {
            if (frames == null || !frames.TryGetValue(role, out var stack)) return;
            if (stack.Count > 0) stack.Pop();
            if (stack.Count == 0) frames.Remove(role);
        }
        internal static void Capture(Role role, int attribute, float delta)
        {
            if (frames == null || !frames.TryGetValue(role, out var stack) || stack.Count == 0) return;
            stack.Peek().Attribute = attribute; stack.Peek().Delta = delta;
        }

        internal static IEnumerable<BaseIncreaser> Chain(BaseIncreaser node)
        {
            for (; node != null; node = node.parent) yield return node;
        }

        internal static Settlement BeforeSet(Role role, int attribute, float old, float value, bool updating)
        {
            if (rewarding > 0 || TraitMechanicPatches.IsFreeAction || !NewEffectPatches.Consumable(attribute)) return null;
            float delta = value - old;
            if (updating)
            {
                if (frames == null || !frames.TryGetValue(role, out var stack) || stack.Count == 0) return null;
                var frame = stack.Peek();
                if (frame.Attribute != attribute || frame.Used) return null;
                frame.Used = true; delta = frame.Delta;
            }
            if (!(delta < 0)) return null;
            var rewards = Chain(role.IncCtrl.Get(RewardType, 0)).OfType<IncreaserOther>().ToList();
            if (rewards.Count == 0) return null;
            var reducers = Chain(role.IncCtrl.Get(NewEffectPatches.CostReduction, attribute)).OfType<IncreaserOther>().ToList();
            var result = new Settlement { Old = old, Cost = -delta, Sum = role.IncCtrl.GetValue(NewEffectPatches.CostReduction, attribute),
                Rewards = new List<Tuple<float, float, string>>() };
            foreach (var reward in rewards)
            {
                float rate = reward.GetSelfValue();
                if (rate <= 0 || string.IsNullOrEmpty(reward.format)) continue;
                float own = Math.Max(0, reducers.Where(r => r.format == reward.format).Sum(r => r.GetSelfValue()));
                if (own > 0) result.Rewards.Add(Tuple.Create(own, rate, reward.tag));
            }
            return result.Rewards.Count == 0 ? null : result;
        }

        internal static void AfterSet(Role role, int attribute, Settlement state)
        {
            if (state == null) return;
            float actual = Math.Max(0, state.Old - role.GetAttr(attribute, true));
            rewarding++;
            try
            {
                foreach (var reward in state.Rewards)
                {
                    float without = state.Cost * Math.Max(0, 1 - state.Sum + reward.Item1);
                    // Clamp the counterfactual too: amounts that could never be paid
                    // are not savings. In particular, an empty attribute earns nothing.
                    without = Math.Max(0, state.Old - role.ClampAttrValue(attribute, state.Old - without, 0));
                    float saved = Math.Max(0, without - actual);
                    if (saved > 0)
                        role.UpdateAttr(Attributes[UnityEngine.Random.Range(0, Attributes.Length)], saved * reward.Item2, 1, reward.Item3, 2);
                }
            }
            finally { rewarding--; }
        }
    }
}
