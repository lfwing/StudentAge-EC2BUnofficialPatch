using System;
using System.Collections.Generic;
using System.Linq;
using Config;
using Effect;
using HarmonyLib;
using Increase;
using MessagePack;
using Multiple;
using Sdk;
using TheEntity;

internal static partial class EffectExtensionCheck
{
    private static int randomIndex, presentations, loadingQueries, masteryHints;
    private static float randomRoll;
    private static RoundModel traitRound;
    private static Action pendingSocial;
    private static bool CaptureSocialEvent(Action _callback) { pendingSocial = _callback; return false; }
    private static bool Pick(int minInclusive, int maxExclusive, ref int __result) { __result = minInclusive + randomIndex % (maxExclusive - minInclusive); return false; }
    private static bool Roll(ref float __result) { __result = randomRoll; return false; }
    private static bool Loading(Action _loadedCallback, Action _closeCallback) { presentations++; _loadedCallback?.Invoke(); _closeCallback?.Invoke(); return false; }
    private static bool Mastery(Action _callback) { masteryHints++; _callback?.Invoke(); return false; }
    private static bool NoActionEvent(ref ValueTuple<int,string> __result) { loadingQueries++; __result = (0, null); return false; }
    private static Effector Block(params float[][] rows) { var e = CommonEvtMgr.GenEffector(rows.Select(r => r.ToList()).ToList()); e.Run(); return e; }
    private static Role Npc(int id, float favor, int relation = 6)
    {
        var npc = new Role(); Set(npc, "id", id); Set(npc, "Relation", relation); Set(npc, "Favor", favor);
        model.roleDict[id] = npc;
        Cfg.PersonCfgMap[id] = new PersonCfg {id=id, name="测试"+id, init=new List<int>{2}};
        return npc;
    }
    private static ActionSubData ActionFixture(int id = 99001, int attr = 2, float amount = 5, int type = 0, int need = 0)
    {
        Cfg.ActionCfgMap[id] = new ActionCfg {id=id, name="测试行动", type=type, needExp=need, next=0, cnt=0, anime="", icon="",
            effect=new List<List<float>>{new List<float>{1,1,attr,amount}}, expReward=new List<List<float>>(),
            cost=new List<List<float>>{new List<float>{7,10}}};
        return new ActionSubData {id=id};
    }
    private static void Do(ActionSubData data)
    {
        try { Singleton<RoleMgr>.Ins.GetActionData().Action(data); }
        catch { Console.Error.WriteLine("action state cnt="+data.totalCnt+" presentations="+presentations+" queries="+loadingQueries); throw; }
    }
    private static float[] Attrs(Role role) => new[] {0,1,2,3,4,7,11,30}.Select(id=>role.GetAttr(id)).ToArray();

    private static void SetupTraitFixture(Harmony fixture)
    {
        // Only presentation, randomness and unrelated achievement/intent sinks are
        // substituted. Native action counters, mastery, CFG effects and costs run.
        fixture.Patch(AccessTools.Method(typeof(UnityEngine.Random), "Range", new[]{typeof(int),typeof(int)}), prefix:new HarmonyMethod(typeof(EffectExtensionCheck), nameof(Pick)));
        fixture.Patch(AccessTools.PropertyGetter(typeof(UnityEngine.Random), "value"), prefix:new HarmonyMethod(typeof(EffectExtensionCheck), nameof(Roll)));
        fixture.Patch(AccessTools.Method(typeof(HintHelper), "ShowLoadingResult", new[]{typeof(string),typeof(Action),typeof(Action),typeof(string),typeof(string),typeof(string),typeof(int),typeof(int),typeof(int),typeof(int),typeof(int)}), prefix:new HarmonyMethod(typeof(EffectExtensionCheck), nameof(Loading)));
        fixture.Patch(AccessTools.Method(typeof(HintHelper), "ShowResult"), prefix:new HarmonyMethod(typeof(EffectExtensionCheck), nameof(Mastery)));
        fixture.Patch(AccessTools.Method(typeof(AudioMgrEx), "PlayUISound", new[]{typeof(int),typeof(float)}), prefix:new HarmonyMethod(typeof(EffectExtensionCheck), nameof(NoRecords)));
        fixture.Patch(AccessTools.Method(typeof(ActionData), "GetActionLoadingEvt"), prefix:new HarmonyMethod(typeof(EffectExtensionCheck), nameof(NoActionEvent)));
        fixture.Patch(AccessTools.Method(typeof(RoleMgr), "UpdateConditionData"), prefix:new HarmonyMethod(typeof(EffectExtensionCheck), nameof(NoRecords)));
        fixture.Patch(AccessTools.Method(typeof(GlobalMgr), "CheckAchievement"), prefix:new HarmonyMethod(typeof(EffectExtensionCheck), nameof(NoRecords)));
        fixture.Patch(AccessTools.Method(typeof(CommonEvtMgr), "ShowEvent"), prefix:new HarmonyMethod(typeof(EffectExtensionCheck), nameof(CaptureSocialEvent)));
        fixture.Patch(AccessTools.Method(typeof(ActionData), "RunSocialEffect"), prefix:new HarmonyMethod(typeof(EffectExtensionCheck), nameof(NoRecords)));
        typeof(Cfg).GetProperty("ActionCfgMap").SetValue(null, new Dictionary<int,ActionCfg>());
        typeof(Cfg).GetProperty("EvtCfgMap").SetValue(null, new Dictionary<int,EvtCfg>());
        var round = new RoundModel(); Set(Singleton<RoundMgr>.Ins, "model", round); Set(round, "totalRound", 1);
        randomIndex = 2; randomRoll = 0;
        traitRound = round;
    }
    private static void VerifyTraitMechanics(Harmony fixture)
    {
        var round = traitRound;

        // Every accepted grammar has text in all native description modes.
        var examples = new[] {new float[]{1,1145,1,361,10,2,2,20}, new float[]{40,1524,1,35}, new float[]{40,1524,2,35},
            new float[]{40,1524,3,25,5}, new float[]{20,1524,1,2,3}, new float[]{999,1145,1,7,20},
            new float[]{999,1145,-1,7,20}, new float[]{999,1145,999,30}, new float[]{999,1145,2,3,4},
            new float[]{999,1145,3,15}, new float[]{999,1524,1,1}};
        var expectedText = new[] {
            "每10点二次元浓度增加2%的情商获取率，最高20%",
            "连续进行同一项具有属性收益的行动时，有35%的概率获得双倍属性",
            "进行具有属性收益的行动时，有35%的概率视为额外无损行动一次",
            "连续进行同一项具有属性收益的行动时，有25%的概率额外消耗相当于精力恢复上限5%的精力，获得三倍属性",
            "每回合首次与某位关注对象完成社交后，心情+2，每回合最多触发3次",
            "减少20%的精力消耗", "增加20%的精力消耗",
            "每次减少某属性消耗后，获得相当于实际减免量30%值的随机一项指定属性",
            "每个强化/批判的方法论/价值观，减少4%的信任消耗",
            "心情带来的三维获取率加成系数额外提升15%",
            "好感度排名第1的关注对象，其已解锁的挚友备选特质同时生效" };
        Func<string,string> plain = text => System.Text.RegularExpressions.Regex.Replace(text,"<[^>]+>","");
        for(int index=0;index<examples.Length;index++) foreach(int mode in new[]{0,1,2,3})
            Check(plain(E(examples[index]).ToString(1,mode))==expectedText[index],"user wording with actual parameters: effect="+index+" mode="+mode);
        var displayNpc=Npc(990106,100);
        Cfg.TraitsCfgMap[99010601]=new TraitsCfg{id=99010601,name="特质显示文本核对",effect=examples.Select(e=>e.ToList()).ToList()};
        Set(displayNpc,"Trait",99010601);
        var displayView=new View.Main.DetailSocialView(); Set(displayView,"role",displayNpc);
        foreach(int mode in new[]{0,1,2,3})
        {
            var card=plain(CommonEvtMgr.ToEffectorStr(Cfg.TraitsCfgMap[99010601].effect,null,0,0,1,mode,null));
            Check(expectedText.All(text=>card.Contains(text)),"native card text uses all requested templates: mode="+mode);
            var detail=(System.Collections.IEnumerable)AccessTools.Method(typeof(View.Main.DetailSocialView),"GetTraitEffect").Invoke(displayView,new object[]{mode,5});
            var texts=detail.Cast<object>().Skip(1).Select(row=>plain((string)AccessTools.Field(row.GetType(),"txt").GetValue(row))).ToArray();
            Check(texts.SequenceEqual(expectedText),"native detail rows use exact requested templates: mode="+mode);
        }
        Check(plain(E(1,1145,1,7,2.5f,3,1.25f,12.5f).ToString(1,1))=="每2.5点精力增加1.25%的信任获取率，最高12.5%","attribute names and fractional parameters come from actual arguments");
        foreach(var row in new[]{new float[]{1,1145,361,10,2,2,20},new float[]{40,1524,3,25},new float[]{40,1524,2,101},
            new float[]{20,1524,1,2,1.5f}, new float[]{999,1145,-1,1,20},new float[]{999,1524,1,0},new float[]{999,1145,999,-1}})
        {
            var invalid = E(row); invalid.Run();
            Check(invalid.ToString().Contains("此项不生效") && invalid.GetBaseIncreaserUids().Count==0,"malformed new grammar visible/inert: "+string.Join(",",row));
        }

        Role role = Reset(); role.SetAttr(7,100);
        Run(999,1145,-1,7,20); Equal(role.GetRealAddValue(7,0,-10,1,2),-12,"increase is separate negative mode");
        Run(999,1145,1,7,20); Equal(role.GetRealAddValue(7,0,-10,1,2),-10,"increase20 minus reduction20 cancel additively");
        Run(999,1145,1,7,100); Equal(role.GetRealAddValue(7,0,-10,1,2),0,"sum reductions before clamping final coefficient");
        role.UpdateAttr(7,-10); Equal(role.GetAttr(7),100,"over100 reduction never restores resource");
        role = Reset(); role.SetAttr(7,11); Run(999,1145,-1,7,20);
        Check(!Singleton<RoleMgr>.Ins.HasEnoughCost(7,10),"increased cost applies to affordability");
        role.UpdateAttr2(7,-5); Equal(role.GetAttr(7),5,"UpdateAttr2 increased cost once");
        role.SetAttr(7,0); Equal(role.GetAttr(7),-1,"direct set increased loss preserves native energy debt");

        role = Reset(); model.studyData = new StudyData();
        Set(model.studyData,"methods",new List<MethodData>{new MethodData{srcId=1,mode=1},new MethodData{srcId=2,mode=2},new MethodData{srcId=3,mode=0}});
        model.valueviewData = new ValueviewData(); model.valueviewData.groups[1]=new ValueviewLineData();
        model.valueviewData.groups[1].nodes.Add(new ValueviewNodeData{srcId=1,mode=1,state=ValueviewNodeState.Enable});
        Run(999,1145,2,3,10); Equal(role.GetRealAddValue(3,0,-10,1,2),-7,"methodologies AND valueviews counted in separate domains");

        // Re-entrant independent effect trees must not share an ownership group.
        var rootTree=E(999,1145,1,7,20); var nestedTree=E(999,1145,999,30);
        Call("Features.Effects.CostRewardPatches","BeginEffect",rootTree);
        string outerGroup=(string)T("Features.Effects.CostRewardPatches").GetProperty("Group",All).GetValue(null);
        Call("Features.Effects.CostRewardPatches","BeginEffect",nestedTree);
        Check(outerGroup!=(string)T("Features.Effects.CostRewardPatches").GetProperty("Group",All).GetValue(null),"nested independent trees have separate saving ownership");
        Call("Features.Effects.CostRewardPatches","EndEffect",new object[]{null});
        Check(outerGroup==(string)T("Features.Effects.CostRewardPatches").GetProperty("Group",All).GetValue(null),"nested effect restores outer saving ownership");
        Call("Features.Effects.CostRewardPatches","EndEffect",new object[]{null});

        foreach(int pick in Enumerable.Range(0,8))
        {
            role=Reset(); role.SetAttr(7,100); randomIndex=pick;
            Block(new float[]{999,1145,1,7,20},new float[]{999,1145,999,30});
            var before=Attrs(role); role.UpdateAttr(7,-30); var after=Attrs(role);
            for(int i=0;i<8;i++) Equal(after[i]-before[i],(i==5?-24:0)+(i==pick?1.8f:0),"own reduction reward pool="+pick+" attr="+i);
        }
        role=Reset(); role.SetAttr(7,100); randomIndex=2;
        Run(999,1145,1,7,10);
        Block(new float[]{999,1145,1,7,20},new float[]{999,1145,999,30});
        role.UpdateAttr(7,-30); Equal(role.GetAttr(2),1.8f,"reward excludes unrelated ten percent saving"); Equal(role.GetAttr(7),79,"total deduction includes both reducers");
        Set(role,"IncCtrl",MessagePackSerializer.Deserialize<RoleIncreaserCtrl>(MessagePackSerializer.Serialize(role.IncCtrl)));
        role.UpdateAttr2(7,-30); Equal(role.GetAttr(2),3.6f,"reward ownership persists after controller serialization");
        role.SetAttr(7,28); Equal(role.GetAttr(2),5.4f,"direct setter rewards own saving once");
        float previous=role.GetAttr(2); role.GetRealAddValue(7,0,-30,1,2); Singleton<RoleMgr>.Ins.HasEnoughCost(7,30);
        Equal(role.GetAttr(2),previous,"preview and affordability do not grant reward");
        role=Reset(); role.SetAttr(7,100); Run(999,1145,1,7,100);
        Block(new float[]{999,1145,1,7,20},new float[]{999,1145,999,30});
        role.UpdateAttr(7,-30); Equal(role.GetAttr(2),0,"own contribution zero when other reductions already cover full cost");
        role=Reset(); role.SetAttr(11,1);
        Block(new float[]{999,1145,1,11,20},new float[]{999,1145,999,30});
        role.UpdateAttr(11,-30); Equal(role.GetAttr(2),0,"unpayable clipped cost does not mint saving");
        role=Reset(); role.SetAttr(7,100);
        Block(new float[]{999,1145,1,7,20},new float[]{999,1145,999,30});
        role.AddEffect(RoleIncType.AttrMul,2,new MultiplierAttr(null){attrId=2,value=.5f},null);
        role.UpdateAttr(7,-30); Equal(role.GetAttr(2),2.7f,"reward goes through native attribute gain rate");

        role=Reset(); role.SetAttr(7,100); randomIndex=2;
        Block(new float[]{999,1145,1,7,20},new float[]{999,1145,-1,7,20},new float[]{999,1145,999,30});
        role.UpdateAttr(7,-30); Equal(role.GetAttr(2),0,"same trait's increase cancels its reduction contribution");
        role=Reset(); role.SetAttr(7,100);
        Block(new float[]{999,1145,999,30},new float[]{999,1145,1,7,20});
        role.UpdateAttr(7,-30); Equal(role.GetAttr(2),1.8f,"reward before reducer in effect array still shares ownership");

        role=Reset(); role.SetAttr(7,100); var data=ActionFixture(); Run(40,1524,1,100);
        Do(data); Equal(role.GetAttr(2),5,"continuous first action grants normal reward");
        Do(data); Equal(role.GetAttr(2),15,"continuous second action grants double reward");
        Equal(data.totalCnt,2,"double rewards do not count another action");
        var otherData=ActionFixture(99002); Do(otherData); Do(data);
        Equal(role.GetAttr(2),25,"switching action breaks continuous streak");
        Set(role,"IncCtrl",MessagePackSerializer.Deserialize<RoleIncreaserCtrl>(MessagePackSerializer.Serialize(role.IncCtrl)));
        Do(data); Equal(role.GetAttr(2),35,"continuous state survives controller serialization");
        role=Reset(); role.SetAttr(7,20); data=ActionFixture(); Run(40,1524,1,100);
        role.AddEffect(RoleIncType.AttrAddInc,2,new IncreaserAttr(null){attrId=2,value=1},null);
        Do(data); var unaffordable=ActionFixture(99002); Cfg.ActionCfgMap[99002].cost[0][1]=100;
        Do(unaffordable); Do(data);
        Equal(role.GetAttr(2),18,"double repeats complete native attribute grant including flat increment; failed different action does not break streak");
        Equal(unaffordable.totalCnt,0,"failed action does not count as execution");
        role=Reset(); role.SetAttr(7,100); data=ActionFixture(); Run(40,1524,1,35); randomRoll=.35f;
        Do(data); Do(data); Equal(role.GetAttr(2),10,"probability upper boundary fails");
        randomRoll=.349f; Do(data); Equal(role.GetAttr(2),20,"probability below threshold succeeds");
        Cfg.ActionCfgMap[data.id].evtId=1;
        Check(!(bool)Call("Features.Effects.TraitMechanicPatches","Eligible",Cfg.ActionCfgMap[data.id]),"event actions excluded");
        Cfg.ActionCfgMap[data.id].evtId=0; Cfg.ActionCfgMap[data.id].minigameId=1;
        Check(!(bool)Call("Features.Effects.TraitMechanicPatches","Eligible",Cfg.ActionCfgMap[data.id]),"minigame actions excluded");

        role=Reset(); role.SetAttr(7,100); role.SetAttr(9,200); data=ActionFixture(); Run(999,1145,1,7,20); Run(40,1524,3,100,5);
        Do(data); Do(data); Equal(role.GetAttr(2),20,"triple applies only after first consecutive action");
        Equal(role.GetAttr(7),76,"triple recovery-cap charge receives ordinary energy reduction"); Equal(data.totalCnt,2,"triple does not increment action count");
        role=Reset(); role.SetAttr(7,10); role.SetAttr(9,200); data=ActionFixture(); Run(40,1524,3,100,5);
        Do(data); role.SetAttr(7,10); Do(data); Equal(role.GetAttr(2),10,"insufficient extra energy grants no unpaid triple");

        role=Reset(); role.SetAttr(7,100); role.SetAttr(0,100); role.SetAttr(30,100); data=ActionFixture(99001,2,5,4,10);
        Cfg.ActionCfgMap[data.id].cost.Add(new List<float>{0,5}); Cfg.ActionCfgMap[data.id].cost.Add(new List<float>{30,5});
        Cfg.ActionCfgMap[data.id].effect.Add(new List<float>{1,1,3,-2}); role.SetAttr(3,50);
        Run(40,1524,2,100); role.SetToggle(9034,1,null); presentations=loadingQueries=0;
        Do(data); Equal(role.GetAttr(2),10,"free repeat reruns base reward"); Equal(data.totalCnt,2,"free repeat advances total action count");
        Equal(data.cnt,2,"free repeat advances current round count"); Equal(data.yearCnt,2,"free repeat advances year count");
        Equal(data.practiceCnt,2,"free repeat advances native mastery"); Equal(role.GetAttr(7),90,"free repeat charges no extra energy");
        Equal(role.GetAttr(0),95,"free repeat charges no extra mood"); Equal(role.GetAttr(30),95,"free repeat charges no extra money");
        Equal(role.GetAttr(3),48,"negative attribute in effects not repeated by free action");
        Equal(presentations,1,"free repeat opens no additional action presentation"); Equal(loadingQueries,1,"free repeat skips random event selection");
        Equal(Singleton<RoleMgr>.Ins.GetActionData().GetActionCnt(data.id).Item2,0,"fixture actions not registered yet");
        Singleton<RoleMgr>.Ins.GetActionData().actionDict[1]=new List<ActionSubData>{data};
        Equal(Singleton<RoleMgr>.Ins.GetActionData().GetActionCnt(data.id).Item2,2,"native action-count prerequisite path sees free action");
        Equal(Singleton<RoleMgr>.Ins.GetActionData().doJobCntThisRound,2,"free work action counted twice");
        role=Reset(); role.SetAttr(7,100); data=ActionFixture(99001,7,20); Run(40,1524,2,100); Do(data);
        Equal(role.GetAttr(7),130,"food recovery included: one cost and two recovery rewards");
        role=Reset(); role.SetAttr(7,100); data=ActionFixture(99001,2,5,0,2); role.SetToggle(9034,1,null); Run(40,1524,2,100); masteryHints=0;
        Do(data); Check(data.isJingtong && data.practiceCnt==2 && masteryHints==1,"free action crosses native mastery threshold once");

        role=Reset(); var socialEffect=Run(20,1524,1,2,3); var npc=Npc(900,100);
        Call("Features.Effects.TraitMechanicPatches","SocialComplete",npc); Call("Features.Effects.TraitMechanicPatches","SocialComplete",npc);
        Equal(role.GetAttr(0),2,"social first completion per followed character");
        foreach(int id in new[]{901,902,903}) Call("Features.Effects.TraitMechanicPatches","SocialComplete",Npc(id,100));
        Equal(role.GetAttr(0),6,"social round cap");
        Set(role,"IncCtrl",MessagePackSerializer.Deserialize<RoleIncreaserCtrl>(MessagePackSerializer.Serialize(role.IncCtrl)));
        Call("Features.Effects.TraitMechanicPatches","SocialComplete",npc); Equal(role.GetAttr(0),6,"social cap/seen IDs survive serialization");
        role.RemoveEffect(socialEffect.GetBaseIncreaserUids()); Run(20,1524,1,2,3);
        Call("Features.Effects.TraitMechanicPatches","SocialComplete",Npc(907,100));
        Equal(role.GetAttr(0),6,"switching away and back cannot reset social round cap");
        Set(round,"totalRound",2); Call("Features.Effects.TraitMechanicPatches","SocialComplete",npc); Equal(role.GetAttr(0),8,"new round resets per-character social state");
        Call("Features.Effects.TraitMechanicPatches","SocialComplete",Npc(904,100,0)); Equal(role.GetAttr(0),8,"unfollowed character excluded from social reward");

        role=Reset(); Run(20,1524,1,2,3); npc=Npc(905,100); pendingSocial=null;
        Call("Features.Effects.TraitMechanicPatches","RunSocialEventEffect",Singleton<RoleMgr>.Ins.GetActionData(),npc);
        Equal(role.GetAttr(0),0,"event social start does not grant completion reward");
        Call("Features.Effects.TraitMechanicPatches","ShowSocialEvent",Singleton<CommonEvtMgr>.Ins,1,1f,null,0,true,null,npc.id);
        Equal(role.GetAttr(0),0,"event social reward waits for native completion callback");
        Check(pendingSocial!=null,"event social installs a completion callback"); pendingSocial();
        Equal(role.GetAttr(0),2,"event social completion grants reward once"); pendingSocial(); Equal(role.GetAttr(0),2,"event social repeated callback cannot reward same target twice");

        Cfg.EvtCfgMap[99111]=new EvtCfg{id=99111,type=0,displayType=0,content="",talkId=new List<int>()};
        Call("Features.Effects.TraitMechanicPatches","ShowSocialEvent",Singleton<CommonEvtMgr>.Ins,99111,1f,null,0,true,null,Npc(906,50).id);
        Equal(role.GetAttr(0),4,"effect-only social event grants synchronous completion reward");

        role=Reset(); var first=Npc(910,200); var second=Npc(911,100); Npc(912,999,0);
        foreach(int id in new[]{91001,91002,91003,91004,91005,91101}) Cfg.TraitsCfgMap[id]=new TraitsCfg{id=id,name="候选"+id,
            effect=new List<List<float>>{new List<float>{999,1145,1,7,id==91101?30:10}}};
        Set(first,"Traits",new List<int>{91001,91002,91003,91004,91005}); Set(first,"Trait",91001); Set(first,"Trait2",91005);
        Set(second,"Traits",new List<int>{91101});
        role.AddEffect((RoleIncType)195050,7,new IncreaserAttr(null){attrId=7,value=.1f},null);
        var insight=Run(999,1524,1,1);
        Equal(role.GetRealAddValue(7,0,-10,1,2),-6,"insight adds other three visible unlocked cards and skips selected/fifth/trait2");
        Set(first,"Relation",4); Equal(role.GetRealAddValue(7,0,-10,1,2),-9,"insight does not bypass relationship gate");
        Set(first,"Relation",6); Equal(role.GetRealAddValue(7,0,-10,1,2),-6,"relationship recovery restores candidates");
        Set(second,"Favor",300f); Equal(role.GetRealAddValue(7,0,-10,1,2),-6,"ranking change swaps borrowed candidate set");
        Set(second,"Relation",0); Equal(role.GetRealAddValue(7,0,-10,1,2),-6,"unfollow refreshes ranking without stale effects");
        Set(first,"Traits",new List<int>{91001,91002,91003}); Equal(role.GetRealAddValue(7,0,-10,1,2),-7,"candidate list replacement withdraws old candidate");
        Cfg.TraitsCfgMap[91003].effect.Add(new List<float>{999,1524,1,1});
        Set(first,"Traits",new List<int>{91001,91003}); Equal(role.GetRealAddValue(7,0,-10,1,2),-8,"recursive insight entry excluded while other candidate effects survive");
        Set(role,"IncCtrl",MessagePackSerializer.Deserialize<RoleIncreaserCtrl>(MessagePackSerializer.Serialize(role.IncCtrl)));
        Equal(role.GetRealAddValue(7,0,-10,1,2),-8,"borrowed UIDs survive serialization without duplicate registration");
        role.RemoveEffect(insight.GetBaseIncreaserUids()); Equal(role.GetRealAddValue(7,0,-10,1,2),-9,"removing insight withdraws all borrowed UIDs");
        Run(999,1524,1,2); Equal(role.GetRealAddValue(7,0,-10,1,2),-9,"absent favor rank grants no candidates");
        // Leave an actually native borrowed gain installed for the disabled-module test.
        role=Reset(); first=Npc(920,100);
        Cfg.TraitsCfgMap[92001]=new TraitsCfg{id=92001,name="原版获取率候选",effect=new List<List<float>>{new List<float>{1,10,2,.1f}}};
        Set(first,"Traits",new List<int>{92001}); Run(999,1524,1,1);
        float nativeBorrowed=role.GetRealAddValue(2,0,100,1,2);
        Equal(nativeBorrowed,110,"insight may borrow native numeric trait effects");
        Set(role,"IncCtrl",MessagePackSerializer.Deserialize<RoleIncreaserCtrl>(MessagePackSerializer.Serialize(role.IncCtrl)));
    }
}
