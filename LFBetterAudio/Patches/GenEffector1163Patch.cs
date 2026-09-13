using System.Collections.Generic;
using Effect;
using LFBetterAudio.Effects;

namespace LFBetterAudio.Patches
{
    /// <summary>
    /// 正常游戏 EFFECT 工厂的精确入口。
    /// 此类不使用 HarmonyPatch Attribute，由 Plugin 手动安装并由持久控制器保活。
    /// </summary>
    internal static class GenEffector1163Patch
    {
        internal static bool Prefix(
            List<float> __0,
            Effector __1,
            int __2,
            int __3,
            ref Effector __result)
        {
            // 同一条 1163 已在 DoText 开始前直接执行时，DoTextEnd 构造原版 EFFECT 链
            // 只需保留它之前已构造的 Effector。其他原版 EFFECT 的顺序和时机不变。
            if (Early1163ExecutionTracker.Consume(__0))
            {
                __result = __1;
                return false;
            }

            bool isCustom = BetterAudioEffectEncoding.TryParse(
                __0,
                out BetterAudioEffectRequest request,
                out string error);

            if (!isCustom)
            {
                return true;
            }

            if (error != null)
            {
                Plugin.LogEffectError(error, __0);
                __result = __1;
                return false;
            }

            __result = new EffectorBetterAudio(__1, __0, request)
            {
                toRoleId = __2,
                fromRoleId = __3
            };
            return false;
        }
    }
}
