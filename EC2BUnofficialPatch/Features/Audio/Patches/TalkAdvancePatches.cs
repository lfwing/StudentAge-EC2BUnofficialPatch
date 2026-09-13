using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using LFBetterAudio.Runtime;
using Sdk;
using UnityEngine;
using View.Evt;

namespace LFBetterAudio.Patches
{
    /// <summary>
    /// 只在当前 1163 会话确实需要区分自动推进时，才给原版 TimerMgr 回调附加来源标记。
    /// 没有相关 1163 会话时，原版 Action、计时器和自动模式逻辑完全不变。
    /// </summary>
    [HarmonyPatch(
        typeof(TimerMgr),
        nameof(TimerMgr.Delay),
        new Type[] { typeof(Action), typeof(float), typeof(int) })]
    internal static class AutoAdvanceDelayOriginPatch
    {
        private sealed class CallbackState
        {
            internal NewTalkView Owner;
            internal Action Wrapper;
        }

        private static readonly ConditionalWeakTable<NewTalkView, CallbackState> States =
            new ConditionalWeakTable<NewTalkView, CallbackState>();

        private static void Prefix(ref Action __0)
        {
            Action original = __0;
            if (original == null || !(original.Target is NewTalkView owner) ||
                original.Method.Name != nameof(NewTalkView.OnClickNext))
            {
                return;
            }

            BetterAudioController controller = BetterAudioController.Instance;
            if (controller == null || !controller.ShouldTrackAutomaticAdvance(owner))
            {
                return;
            }

            CallbackState state = States.GetValue(owner, CreateState);
            __0 = state.Wrapper;
        }

        private static CallbackState CreateState(NewTalkView owner)
        {
            var state = new CallbackState
            {
                Owner = owner
            };
            state.Wrapper = () => InvokeAutomatic(state);
            return state;
        }

        private static void InvokeAutomatic(CallbackState state)
        {
            NewTalkView owner = state?.Owner;
            if (owner == null)
            {
                return;
            }

            // 该回调最初由自动模式登记。若玩家之后关闭了自动模式，
            // 只丢弃这条在 1163 会话期间登记的旧回调，不接管其他原版计时器。
            if (!NewTalkAdvanceOriginDetector.IsAutoEnabled(owner))
            {
                return;
            }

            BetterAudioController controller = BetterAudioController.Instance;
            if (controller == null || !controller.ShouldTrackAutomaticAdvance(owner))
            {
                owner.OnClickNext();
                return;
            }

            controller.BeginAdvance(owner, TalkAdvanceOrigin.Automatic);
            try
            {
                owner.OnClickNext();
            }
            finally
            {
                controller.EndAdvance(owner);
            }
        }
    }

    internal static class NewTalkAdvanceOriginDetector
    {
        private static readonly FieldInfo EnableAutoTalkField =
            AccessTools.Field(typeof(NewTalkView), "enableAutoTalk");

        internal static TalkAdvanceOrigin Detect(NewTalkView view)
        {
            if (Time.timeScale > 1.001f)
            {
                return TalkAdvanceOrigin.FastForward;
            }

            return IsAutoEnabled(view)
                ? TalkAdvanceOrigin.Automatic
                : TalkAdvanceOrigin.Manual;
        }

        internal static bool IsAutoEnabled(NewTalkView view)
        {
            try
            {
                object value = EnableAutoTalkField?.GetValue(view);
                return value is bool enabled && enabled;
            }
            catch
            {
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(NewTalkView), nameof(NewTalkView.OnClickNext))]
    internal static class NewTalkClickContextPatch
    {
        private static void Prefix(NewTalkView __instance, out bool __state)
        {
            BetterAudioController controller = BetterAudioController.Instance;
            __state = controller != null;
            controller?.BeginAdvance(
                __instance,
                NewTalkAdvanceOriginDetector.Detect(__instance));
        }

        private static void Postfix(NewTalkView __instance, bool __state)
        {
            if (__state)
            {
                BetterAudioController.Instance?.EndAdvance(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(PreviewTalkView), nameof(PreviewTalkView.OnClickNext))]
    internal static class PreviewTalkClickContextPatch
    {
        private static void Prefix(PreviewTalkView __instance, out bool __state)
        {
            BetterAudioController controller = BetterAudioController.Instance;
            __state = controller != null;
            controller?.BeginAdvance(__instance, TalkAdvanceOrigin.Manual);
        }

        private static void Postfix(PreviewTalkView __instance, bool __state)
        {
            if (__state)
            {
                BetterAudioController.Instance?.EndAdvance(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(NewTalkView), nameof(NewTalkView.NextTalk))]
    internal static class NewTalkNextPatch
    {
        private static bool Prefix(NewTalkView __instance)
        {
            BetterAudioController controller = BetterAudioController.Instance;
            return controller == null || controller.HandleNextTalkAttempt(__instance);
        }
    }

    [HarmonyPatch(typeof(PreviewTalkView), nameof(PreviewTalkView.NextTalk))]
    internal static class PreviewTalkNextPatch
    {
        private static bool Prefix(PreviewTalkView __instance)
        {
            BetterAudioController controller = BetterAudioController.Instance;
            return controller == null || controller.HandleNextTalkAttempt(__instance);
        }
    }
}
