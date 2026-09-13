using System.Collections.Generic;
using Config;
using HarmonyLib;
using View.Evt;

namespace LFBetterAudio.Preview
{
    internal static class PreviewTalkAccess
    {
        private static readonly System.Reflection.FieldInfo CfgField = AccessTools.Field(typeof(PreviewTalkView), "cfg");
        private static readonly System.Reflection.FieldInfo LastEffectCfgIdField = AccessTools.Field(typeof(PreviewTalkView), "lastEffectCfgId");
        private static readonly System.Reflection.FieldInfo TalkCfgMapField = AccessTools.Field(typeof(PreviewTalkView), "talkCfgMap");
        private static readonly System.Reflection.FieldInfo AudioCfgMapField = AccessTools.Field(typeof(PreviewTalkView), "audioCfgMap");
        private static readonly System.Reflection.FieldInfo PersonCfgMapField = AccessTools.Field(typeof(PreviewTalkView), "personCfgMap");
        private static readonly System.Reflection.FieldInfo GenderField = AccessTools.Field(typeof(PreviewTalkView), "gender");

        public static TalkCfg GetCurrentCfg(PreviewTalkView view)
        {
            return CfgField?.GetValue(view) as TalkCfg;
        }

        public static int GetLastEffectCfgId(PreviewTalkView view)
        {
            object value = LastEffectCfgIdField?.GetValue(view);
            return value is int id ? id : 0;
        }

        public static Dictionary<int, TalkCfg> GetTalkCfgMap(PreviewTalkView view)
        {
            return TalkCfgMapField?.GetValue(view) as Dictionary<int, TalkCfg>;
        }

        public static Dictionary<int, AudioCfg> GetAudioCfgMap(PreviewTalkView view)
        {
            return AudioCfgMapField?.GetValue(view) as Dictionary<int, AudioCfg>;
        }

        public static Dictionary<int, PersonCfg> GetPersonCfgMap(PreviewTalkView view)
        {
            return PersonCfgMapField?.GetValue(view) as Dictionary<int, PersonCfg>;
        }

        public static GenderDefine GetGender(PreviewTalkView view)
        {
            object value = GenderField?.GetValue(view);
            return value is GenderDefine gender ? gender : GenderDefine.Unknown;
        }
    }
}
