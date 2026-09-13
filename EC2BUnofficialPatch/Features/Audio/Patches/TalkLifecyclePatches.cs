using System;
using HarmonyLib;
using LFBetterAudio.Effects;
using LFBetterAudio.Preview;
using LFBetterAudio.Runtime;
using Sdk;
using View.Event;
using View.Evt;

namespace LFBetterAudio.Patches
{
    [HarmonyPatch(typeof(NewTalkView), nameof(NewTalkView.RefreshTalk))]
    internal static class NewTalkRefreshPatch
    {
        private static void Prefix(NewTalkView __instance, int __0)
        {
            EarlyTalkPlan plan = Early1163Execution.PrepareRuntimeIncomingTalk(__instance, __0);
            bool deferSingleTalkEnd = !plan.IsEmpty || plan.HasValidPlayCommand;
            BetterAudioController.Instance?.BeforeTalkRefresh(
                __instance,
                __0,
                deferSingleTalkEnd);
        }

        private static void Postfix(NewTalkView __instance)
        {
            int currentTalkId = RuntimeTalkAccess.TryGetTalkId(__instance);
            BetterAudioController.Instance?.AfterTalkRefresh(__instance, currentTalkId);
        }
    }

    [HarmonyPatch(typeof(CommonTalkView), nameof(CommonTalkView.RefreshTalk))]
    internal static class CommonTalkRefreshLifecyclePatch
    {
        private static void Prefix(CommonTalkView __instance, int __0)
        {
            // CommonTalkView 没有 DoText 提前入口，继续按原逻辑在切换时结束单 Talk 会话。
            BetterAudioController.Instance?.BeforeTalkRefresh(__instance, __0, false);
        }

        private static void Postfix(CommonTalkView __instance)
        {
            BetterAudioController.Instance?.AfterTalkRefresh(__instance, -1);
        }
    }

    [HarmonyPatch(typeof(PreviewTalkView), nameof(PreviewTalkView.RefreshTalk))]
    internal static class PreviewTalkRefreshLifecyclePatch
    {
        private static void Prefix(PreviewTalkView __instance, int __0)
        {
            EarlyTalkPlan plan = Early1163Execution.PreparePreviewIncomingTalk(__instance, __0);
            bool deferSingleTalkEnd = !plan.IsEmpty || plan.HasValidPlayCommand;
            BetterAudioController.Instance?.BeforeTalkRefresh(
                __instance,
                __0,
                deferSingleTalkEnd);
        }

        private static void Postfix(PreviewTalkView __instance)
        {
            int currentTalkId = PreviewTalkAccess.GetCurrentCfg(__instance)?.id ?? 0;
            BetterAudioController.Instance?.AfterTalkRefresh(__instance, currentTalkId);
        }
    }

    [HarmonyPatch(typeof(NewTalkView), nameof(NewTalkView.SpeedUp))]
    internal static class NewTalkSpeedUpGuardPatch
    {
        private static bool Prefix(NewTalkView __instance, bool __0)
        {
            if (!__0)
            {
                return true;
            }

            BetterAudioController controller = BetterAudioController.Instance;
            return controller == null || !controller.ShouldBlockFastForward(__instance);
        }
    }

    [HarmonyPatch(typeof(NewTalkView), "OnClickSkip")]
    internal static class NewTalkDirectSkipGuardPatch
    {
        private static bool Prefix(NewTalkView __instance)
        {
            BetterAudioController controller = BetterAudioController.Instance;
            return controller == null || !controller.ShouldBlockFastForward(__instance);
        }
    }

    [HarmonyPatch(typeof(NewTalkView), nameof(NewTalkView.CloseView))]
    internal static class NewTalkCloseGuardPatch
    {
        private static bool Prefix(NewTalkView __instance)
        {
            BetterAudioController controller = BetterAudioController.Instance;
            if (controller != null && controller.ShouldBlockClose(__instance))
            {
                return false;
            }

            controller?.CleanupForView(__instance, "NewTalkView.CloseView");
            return true;
        }
    }

    [HarmonyPatch(typeof(BaseView), nameof(BaseView.CloseView))]
    internal static class PreviewBaseCloseGuardPatch
    {
        private static bool Prefix(BaseView __instance)
        {
            if (!(__instance is PreviewTalkView))
            {
                return true;
            }

            BetterAudioController controller = BetterAudioController.Instance;
            if (controller != null && controller.ShouldBlockClose(__instance))
            {
                return false;
            }

            controller?.CleanupForView(__instance, "PreviewTalkView.CloseView");
            return true;
        }
    }

    [HarmonyPatch(
        typeof(UIMgr),
        nameof(UIMgr.CloseView),
        new Type[] { typeof(BaseView), typeof(bool), typeof(bool) })]
    internal static class TalkUIMgrCloseGuardPatch
    {
        private static bool Prefix(BaseView __0)
        {
            if (!(__0 is NewTalkView) && !(__0 is PreviewTalkView))
            {
                return true;
            }

            BetterAudioController controller = BetterAudioController.Instance;
            if (controller != null && controller.ShouldBlockClose(__0))
            {
                return false;
            }

            controller?.CleanupForView(__0, $"UIMgr.CloseView({__0.GetType().Name})");
            return true;
        }
    }

    [HarmonyPatch(typeof(NewTalkView), nameof(NewTalkView.OnClose))]
    internal static class NewTalkOnCloseCleanupPatch
    {
        private static void Prefix(NewTalkView __instance)
        {
            BetterAudioController.Instance?.CleanupForView(__instance, "NewTalkView.OnClose");
        }
    }

    [HarmonyPatch(typeof(PreviewTalkView), nameof(PreviewTalkView.OnClose))]
    internal static class PreviewTalkOnCloseCleanupPatch
    {
        private static void Prefix(PreviewTalkView __instance)
        {
            BetterAudioController.Instance?.CleanupForView(__instance, "PreviewTalkView.OnClose");
        }
    }
}
