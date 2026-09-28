using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using EC2BUnofficialPatch.Services.EventCompatibility;

class LogicTests
{
    static int count;
    static void Check(bool pass,string label){if(!pass)throw new Exception(label);count++;Console.WriteLine("PASS "+label);}
    static void Throws(Action action,string label){try{action();}catch(InvalidOperationException){Check(true,label);return;}throw new Exception("Expected failure: "+label);}
    static Dictionary<string,HashSet<int>> Baseline()=>CollisionPlan.Tables.ToDictionary(t=>t,t=>new HashSet<int>{1,2,3});
    static ModInput Mod(string id)
    {
        var m=new ModInput{Id=id};
        m.Tables["EvtCfg"]=JObject.Parse("{\"70001\":{\"id\":70001,\"title\":\"name 70001\",\"talkId\":[70002],\"options\":[70003],\"condition\":[[3,1,70001,1,2],[3,-3,70002],[3,2,70003],[111,1,70001,0,70001],[3,5,70001]],\"effect\":[[50,2,70001,0,70001],[50,1,70001,2],[50,3,70001],[1,1,70001]],\"npc\":70001},\"1\":{\"id\":1,\"talkId\":[70002]}}");
        m.Tables["TalkCfg"]=JObject.Parse("{\"70002\":{\"id\":70002,\"content\":\"70002\",\"nextTalk\":[70002,1],\"option\":[70003],\"roles\":[[3,1,70001]]}}");
        m.Tables["OptionCfg"]=JObject.Parse("{\"70003\":{\"id\":70003,\"talkId\":[70002],\"talkId2\":[70002],\"nextEvtId\":70001}}");
        m.Tables["ItemCfg"]=JObject.Parse("{\"81\":{\"id\":81,\"talkId\":70002,\"effect\":[[50,100,70001]],\"precondition\":[[3,-1,70001]]}}");
        m.Tables["DNDTalkCfg"]=JObject.Parse("{\"82\":{\"id\":82,\"check\":[[3,1,70001]],\"effect\":[[50,1,70001]],\"gameEffect\":[[50,4,70001]]}}");
        return m;
    }
    static void Main()
    {
        var a=Mod("10");var b=Mod("20");var original=JsonConvert.SerializeObject(b.Tables);var saved=new IdRegistry();var plan=new CollisionPlan(new[]{a,b},Baseline(),saved);
        int e=plan.Own("20","EvtCfg",70001),t=plan.Own("20","TalkCfg",70002),o=plan.Own("20","OptionCfg",70003);
        Check(e!=70001&&t!=70002&&o!=70003,"all three collision tables retained");
        Check(plan.Own("10","EvtCfg",70001)==70001,"first owner keeps existing save ID");
        Check(saved.Aliases.Count==0&&saved.Owners.Count==0,"registry input not mutated before commit");
        Check(JsonConvert.SerializeObject(b.Tables)==original,"source mod JSON never mutated");
        var er=plan.Output["20"]["EvtCfg"][e.ToString()];var tr=plan.Output["20"]["TalkCfg"][t.ToString()];var op=plan.Output["20"]["OptionCfg"][o.ToString()];
        Check((int)er["id"]==e&&(int)er["talkId"][0]==t&&(int)er["options"][0]==o,"event key and links remapped");
        Check((int)tr["nextTalk"][0]==t&&(int)tr["nextTalk"][1]==1&&(int)tr["option"][0]==o,"talk chain and sentinels");
        Check((int)op["nextEvtId"]==e&&(int)op["talkId2"][0]==t,"option success failure and next event links");
        Check((int)er["condition"][0][2]==e&&(int)er["condition"][0][3]==1&&(int)er["condition"][0][4]==2,"history IDs independent count unmodified");
        Check((int)er["condition"][1][2]==t&&(int)er["condition"][2][2]==o,"talk and option history remapped");
        Check((int)er["condition"][3][2]==e&&(int)er["condition"][3][4]==70001,"event variable key remapped numeric target retained");
        Check((int)er["condition"][4][2]==70001,"phone message condition not mistaken for event");
        Check((int)er["effect"][0][2]==e&&(int)er["effect"][0][4]==70001,"effect event variable remapped amount retained");
        Check((int)er["effect"][1][2]==e&&(int)er["effect"][1][3]==2,"effect both genders and native target");
        Check((int)er["effect"][2][2]==70001&&(int)er["effect"][3][2]==70001&&(int)er["npc"]==70001,"type IDs and unrelated values preserved");
        Check((string)er["title"]=="name 70001"&&(string)tr["content"]=="70002"&&(int)tr["roles"][0][2]==70001,"text and roles not rewritten");
        var item=plan.Output["20"]["ItemCfg"]["81"];
        Check((int)item["talkId"]==t&&(int)item["precondition"][0][2]==e&&(int)item["effect"][0][2]==e,"references outside event table follow own mod");
        var dnd=plan.Output["20"]["DNDTalkCfg"]["82"];
        Check((int)dnd["check"][0][2]==70001&&(int)dnd["effect"][0][2]==70001&&(int)dnd["gameEffect"][0][2]==e,"DND protocol isolated but native gameEffect mapped");
        Check(plan.Output["20"]["EvtCfg"].ContainsKey("1")&&plan.Own("20","EvtCfg",1)==1,"native override keeps single original slot");
        Check((int)plan.Output["20"]["EvtCfg"]["1"]["talkId"][0]==t,"native override may link own nonnative dialogue");
        foreach(var alias in plan.Registry.Aliases.Values)Check((int)(float)alias==alias,"runtime ID survives native float serialization "+alias);
        var again=new CollisionPlan(new[]{b,a},Baseline(),plan.Registry);
        Check(again.Own("20","EvtCfg",70001)==e&&again.Own("10","EvtCfg",70001)==70001,"mod reorder keeps ID identity");
        var alone=new CollisionPlan(new[]{b},Baseline(),plan.Registry);
        Check(alone.Own("20","EvtCfg",70001)==e,"disabling original owner keeps survivor identity");
        var c=Mod("30");var three=new CollisionPlan(new[]{c,a,b},Baseline(),plan.Registry);
        Check(three.Own("20","EvtCfg",70001)==e&&three.Own("30","EvtCfg",70001)!=e&&three.Own("30","EvtCfg",70001)!=70001,"new earlier mod does not hijack previous history");
        Check(plan.Resolve("unknown","EvtCfg",70001)==70001,"unqualified crossmod lookup remains single global target");
        var nativeAdded=Baseline();nativeAdded["EvtCfg"].Add(70001);
        Throws(()=>new CollisionPlan(new[]{a,b},nativeAdded,plan.Registry),"native update requires migration instead of double triggering");
        var reservedAdded=Mod("40");reservedAdded.Tables["EvtCfg"][e.ToString()]=JObject.Parse("{\"id\":"+e+"}");
        Throws(()=>new CollisionPlan(new[]{a,b,reservedAdded},Baseline(),plan.Registry),"new explicit ID cannot hijack persisted alias");
        var broken=Mod("20");broken.Tables["EvtCfg"]["70001"]["effect"]=JArray.Parse("[[50,10,2,70001]]");
        Throws(()=>new CollisionPlan(new[]{a,broken},Baseline(),new IdRegistry()),"ambiguous delayed effect rejected transactionally");
        var copy=new CollisionPlan(new[]{a,b},Baseline(),JsonConvert.DeserializeObject<IdRegistry>(JsonConvert.SerializeObject(plan.Registry)));
        Check(copy.Own("20","EvtCfg",70001)==e,"persisted registry reload identity");
        // Simulate exact first-wins native mod merge followed by dictionary overwrite of the original baseline.
        foreach(var order in new[]{new[]{a,b},new[]{b,a}})
        {
            var p=new CollisionPlan(order,Baseline(),plan.Registry);var merged=new Dictionary<int,JToken>{{1,new JObject{{"title","native"}}}};var modRows=new Dictionary<int,JToken>();
            foreach(var m in order)foreach(var row in p.Output[m.Id]["EvtCfg"].Properties()){int id=int.Parse(row.Name);if(!modRows.ContainsKey(id))modRows[id]=row.Value;}
            foreach(var row in modRows)merged[row.Key]=row.Value;
            Check(merged.Count==3&&merged.ContainsKey(70001)&&merged.ContainsKey(e),"two added stories and exactly one override after native merge");
            Check((int)merged[1]["talkId"][0]==p.Own(order[0].Id,"TalkCfg",70002),"original override winner follows native enabled order");
        }
        Console.WriteLine("LOGIC_TESTS_OK "+count);
    }
}
