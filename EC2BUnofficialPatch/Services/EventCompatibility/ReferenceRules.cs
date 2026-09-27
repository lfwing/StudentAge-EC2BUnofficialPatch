using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace EC2BUnofficialPatch.Services.EventCompatibility
{
    internal static class ReferenceRules
    {
        static readonly Dictionary<string, string> Direct = new Dictionary<string, string>();
        static readonly HashSet<string> Conditions = new HashSet<string>(new[] { "cond", "conds", "condition", "precondition", "check", "stateCond", "demand", "interactable", "unlock", "appearCond", "unlockCond", "interactCond", "impossible", "gaozhongCond" });
        static readonly HashSet<string> Effects = new HashSet<string>(new[] { "effect", "Effect", "effects", "effect1", "effect2", "effect3", "effect4", "effect5", "cost", "reward", "fail", "win", "success", "usingEffect", "beginEffect", "endEffect", "fixedEffects", "gameEffect", "buff", "debuff" });
        static ReferenceRules()
        {
            Add("EvtCfg", "talkId", "TalkCfg"); Add("EvtCfg", "options", "OptionCfg");
            Add("TalkCfg", "nextTalk nextTalk2", "TalkCfg"); Add("TalkCfg", "option", "OptionCfg");
            Add("OptionCfg", "talkId talkId2", "TalkCfg"); Add("OptionCfg", "nextEvtId", "EvtCfg");
            Add("ActionCfg", "evtId", "EvtCfg"); Add("ActionEvtCfg", "evts", "EvtCfg");
            Add("AnimeConCfg", "evtId", "EvtCfg"); Add("Basketball1On1Cfg", "enableEvtId showEvtId", "EvtCfg");
            Add("ExpoEvtCfg", "evtId", "EvtCfg"); Add("ExpoSiteCfg", "evt", "EvtCfg"); Add("TripSpotCfg", "evtId", "EvtCfg");
            Add("EndingPartCfg", "evt", "EvtCfg");
            Add("CGCfg", "startTalks", "TalkCfg"); Add("GiftEvtCfg", "talkId", "TalkCfg");
            Add("IntentCfg", "failTalk finishTalk", "TalkCfg"); Add("InteractCfg", "talkId", "TalkCfg"); Add("ItemCfg", "talkId", "TalkCfg");
            Add("LoveDrawCfg", "talkId", "TalkCfg"); Add("LoveGreetingCfg", "talkId", "TalkCfg"); Add("NpcActivityCfg", "talkId", "TalkCfg");
            Add("MinigameActionCfg", "loseTalk startTalk winTalk", "TalkCfg"); Add("NegotiationCfg", "talks talks2", "TalkCfg");
            Add("TalkInputMinigameCfg", "talkId jumps", "TalkCfg");
        }
        static void Add(string table, string fields, string target) { foreach (string f in fields.Split(' ')) Direct[table + "." + f] = target; }
        static int Int(JToken t) { return t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) && (double)t == Math.Truncate((double)t) && (double)t >= int.MinValue && (double)t <= int.MaxValue ? (int)t : int.MinValue; }
        static JToken Ids(JToken t, string table, Func<string, int, int> resolve)
        {
            if (t is JArray) return new JArray(((JArray)t).Select(x => Ids(x, table, resolve)));
            int n = Int(t); return n == int.MinValue ? t.DeepClone() : new JValue(resolve(table, n));
        }
        static void At(JArray row, int pos, string table, Func<string, int, int> resolve)
        {
            if (pos >= row.Count) return;
            int n = Int(row[pos]); if (n != int.MinValue) row[pos] = resolve(table, n);
        }
        public static void Rewrite(string table, JObject record, Func<string, int, int> resolve)
        {
            foreach (var p in record.Properties().ToArray())
            {
                string target;
                if (Direct.TryGetValue(table + "." + p.Name, out target)) { if (p.Value.Type != JTokenType.Null) p.Value = Ids(p.Value, target, resolve); continue; }
                if (!(p.Value is JArray)) continue;
                bool dnd = table.StartsWith("DND", StringComparison.Ordinal);
                bool condition = !dnd && Conditions.Contains(p.Name), effect = (!dnd || p.Name == "gameEffect") && Effects.Contains(p.Name);
                if (!condition && !effect) continue;
                foreach (var item in (JArray)p.Value)
                {
                    var row = item as JArray; if (row == null || row.Count < 2) continue;
                    int code = Int(row[0]), sub = Int(row[1]);
                    if (condition && code == 3)
                    {
                        int abs = Math.Abs(sub == int.MinValue ? 0 : sub);
                        target = abs == 1 ? "EvtCfg" : abs == 2 ? "OptionCfg" : abs == 3 || abs == 30 ? "TalkCfg" : null;
                        if (target != null) { At(row, 2, target, resolve); At(row, 3, target, resolve); }
                    }
                    else if (condition && code == 111) At(row, 2, "EvtCfg", resolve);
                    else if (effect && code == 50)
                    {
                        if (sub == 1) { At(row, 2, "EvtCfg", resolve); At(row, 3, "EvtCfg", resolve); }
                        else if (sub == 2 || sub == 4 || sub == 20 || sub == 100) At(row, 2, "EvtCfg", resolve);
                        else if (sub == 10)
                        {
                            // Native constructor also treats this slot as a value; rewriting it changes both meanings.
                            int id = row.Count > 3 ? Int(row[3]) : 0;
                            if (id > 0 && resolve("EvtCfg", id) != id) throw new InvalidOperationException("Effect [50,10] has ambiguous event/value slot; explicit adaptation required");
                        }
                    }
                }
            }
        }
    }
}
