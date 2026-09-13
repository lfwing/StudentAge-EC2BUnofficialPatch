using LFBetterAudio.Effects;
using View.Evt;

namespace LFBetterAudio.Patches
{
    /// <summary>
    /// 三个 Prefix 均由 Plugin 手动精确安装并纳入关键入口保活：
    /// 正常游戏与 Preview 的非空 Talk 在 DoText 前执行 1163；
    /// Preview 空文本 Talk 仍在 RefreshTalk 前执行。
    /// </summary>
    internal static class RuntimeDoTextEffectPatch
    {
        internal static void Prefix(NewTalkView __instance)
        {
            Early1163Execution.ExecuteRuntimeBeforeText(__instance);
        }
    }

    internal static class PreviewDoTextEffectPatch
    {
        internal static void Prefix(PreviewTalkView __instance)
        {
            Early1163Execution.ExecutePreviewBeforeText(__instance);
        }
    }

    internal static class PreviewEmptyTalkEffectPatch
    {
        internal static void Prefix(PreviewTalkView __instance, int __0)
        {
            Early1163Execution.ExecutePreviewEmptyTalk(__instance, __0);
        }
    }
}
