using HarmonyLib;
using LFBetterAudio.Runtime;
using Sdk;
using View.Evt;

namespace LFBetterAudio.Patches
{
    [HarmonyPatch(typeof(NewTalkView), nameof(NewTalkView.OnHotKeyStartPress))]
    internal static class NewTalkHoldStartPatch
    {
        private static bool Prefix(NewTalkView __instance, int __0, ref bool __result)
        {
            BetterAudioController controller = BetterAudioController.Instance;
            if (controller == null || !controller.HandleHoldStart(__instance, __0))
            {
                return true;
            }

            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(NewTalkView), nameof(NewTalkView.OnHotKeyPressing))]
    internal static class NewTalkHoldTickPatch
    {
        private static bool Prefix(NewTalkView __instance, int __0, ref bool __result)
        {
            BetterAudioController controller = BetterAudioController.Instance;
            if (controller == null || !controller.HandleHoldTick(__instance, __0))
            {
                return true;
            }

            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(NewTalkView), nameof(NewTalkView.OnHotKeyInput))]
    internal static class NewTalkHoldReleasePatch
    {
        private static bool Prefix(NewTalkView __instance, int __0, ref bool __result)
        {
            BetterAudioController controller = BetterAudioController.Instance;
            if (controller == null || !controller.HandleHoldRelease(__instance, __0))
            {
                return true;
            }

            __result = true;
            return false;
        }
    }

    // PreviewTalkView 继承 BaseView 的按下/持续按住方法。
    [HarmonyPatch(typeof(BaseView), nameof(BaseView.OnHotKeyStartPress))]
    internal static class PreviewHoldStartPatch
    {
        private static bool Prefix(BaseView __instance, int __0, ref bool __result)
        {
            if (!(__instance is PreviewTalkView))
            {
                return true;
            }

            BetterAudioController controller = BetterAudioController.Instance;
            if (controller == null || !controller.HandleHoldStart(__instance, __0))
            {
                return true;
            }

            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(BaseView), nameof(BaseView.OnHotKeyPressing))]
    internal static class PreviewHoldTickPatch
    {
        private static bool Prefix(BaseView __instance, int __0, ref bool __result)
        {
            if (!(__instance is PreviewTalkView))
            {
                return true;
            }

            BetterAudioController controller = BetterAudioController.Instance;
            if (controller == null || !controller.HandleHoldTick(__instance, __0))
            {
                return true;
            }

            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(PreviewTalkView), nameof(PreviewTalkView.OnHotKeyInput))]
    internal static class PreviewHoldReleasePatch
    {
        private static bool Prefix(PreviewTalkView __instance, int __0, ref bool __result)
        {
            BetterAudioController controller = BetterAudioController.Instance;
            if (controller == null || !controller.HandleHoldRelease(__instance, __0))
            {
                return true;
            }

            __result = true;
            return false;
        }
    }
}
