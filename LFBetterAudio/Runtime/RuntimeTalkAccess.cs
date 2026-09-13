using Config;
using HarmonyLib;
using View.Evt;

namespace LFBetterAudio.Runtime
{
    internal static class RuntimeTalkAccess
    {
        private static readonly System.Reflection.FieldInfo CfgField =
            AccessTools.Field(typeof(NewTalkView), "cfg");
        private static readonly System.Reflection.FieldInfo LastEffectCfgIdField =
            AccessTools.Field(typeof(NewTalkView), "lastEffectCfgId");

        public static TalkCfg GetCurrentCfg(NewTalkView view)
        {
            return view == null ? null : CfgField?.GetValue(view) as TalkCfg;
        }

        public static int GetLastEffectCfgId(NewTalkView view)
        {
            if (view == null)
            {
                return 0;
            }

            object value = LastEffectCfgIdField?.GetValue(view);
            return value is int id ? id : 0;
        }

        public static int TryGetTalkId(NewTalkView view)
        {
            return GetCurrentCfg(view)?.id ?? 0;
        }
    }
}
