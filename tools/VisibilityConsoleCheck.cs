using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Config;
using Effect;
using HarmonyLib;
using Increase;
using MessagePack;
using Sdk;
using TheEntity;
using View.Common;
using View.TheAction;

internal static partial class EffectExtensionCheck
{
    private static void IgnorePostNotification(int _id, bool _active, int _childId) { }
    private static IEnumerable<CodeInstruction> IsolatePostNotification(IEnumerable<CodeInstruction> codes)
    {
        foreach(var code in codes)
        {
            if(code.Calls(AccessTools.Method(typeof(RedpointMgr),"Active")))
                code.operand=AccessTools.Method(typeof(EffectExtensionCheck),nameof(IgnorePostNotification));
            yield return code;
        }
    }
    private const string Visibility = "Features.Mechanics.RoleVisibilityPatches";
    private const string Availability = "Features.Mechanics.RoleAvailabilityPatches";
    private static Dictionary<int,KZoneContentData> Visible(KZoneData data) => (Dictionary<int,KZoneContentData>)Call(Visibility,"VisiblePosts",data);
    private static bool PostVisible(KZoneContentData post) => (bool)Call(Visibility,"PostVisible",post);
    private static Dictionary<int,KZoneCommentData> Comments(KZoneContentData post) => (Dictionary<int,KZoneCommentData>)Call(Visibility,"VisibleComments",post);
    private static List<int> Likes(KZoneContentData post) => (List<int>)Call(Visibility,"VisibleLikes",post);
    private static void VerifyVisibilityAndConsole(string outputDirectory)
    {
        var history = Activator.CreateInstance(T("Features.Optimization.ConsoleCommandHistory"),true);
        Func<int,string,string> move = (d,text) => (string)AccessTools.Method(history.GetType(),"Move").Invoke(history,new object[]{d,text});
        Action<string> add = text => AccessTools.Method(history.GetType(),"Add").Invoke(history,new object[]{text});
        Check(move(-1,"draft")=="draft","empty console history preserves draft");
        add("   "); Check(move(-1,"draft")=="draft","blank submissions are not history");
        add("FIRST Case"); add("second");
        Check(move(-1,"unfinished")=="second","Up recalls latest command");
        Check(move(-1,"second")=="FIRST Case","history preserves exact typed casing");
        Check(move(-1,"FIRST Case")=="FIRST Case","Up stops at first command");
        Check(move(1,"FIRST Case")=="second","Down recalls next command");
        Check(move(1,"second")=="unfinished","Down past latest restores unfinished input");
        Check(move(1,"unfinished")=="unfinished","Down at end preserves draft");
        move(-1,"new draft"); add("third");
        Check(move(-1,"")=="third","submitting resets navigation to newest");
        var console = new Harmony("qa.console-history");
        var consoleModule=Activator.CreateInstance(T("Features.Optimization.ConsoleHistoryModule"),true);
        consoleModule.GetType().GetMethod("Load").Invoke(consoleModule,new object[]{console,null});
        Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(DebugView),"AcceptInput")).Owners.Contains(console.Id),"console submission hook installs on actual game method");
        Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(DebugView),"Update")).Owners.Contains(console.Id),"console keyboard hook installs on actual game method");
        console.UnpatchSelf();

        var main = Reset();
        var npc = Npc(102,100); npc.traitChangeCnt=12;
        Check(!(bool)Call("Features.Effects.TraitCooldownPatches","Enabled"),"trait cooldown remains native without effect");
        var cooldown=Run(20,5418,1,1);
        Check((bool)Call("Features.Effects.TraitCooldownPatches","Enabled"),"20/5418 enables no cooldown");
        foreach(int mode in new[]{0,1,2,3}) Check(E(20,5418,1,1).ToString(1,mode).Contains("特质卡片可随时切换，无冷却限制"),"no cooldown description mode "+mode);
        Check(ActionDescHelper.TraitChange(0,npc).Value.title=="特质卡片可随时切换，无冷却限制","native cooldown tooltip uses new effect wording");
        Check(!ActionDescHelper.TraitChange(1,npc).HasValue,"other tooltip modes remain native");
        Check(npc.traitChangeCnt==12,"effect does not erase last trait change round");
        var ids=cooldown.GetBaseIncreaserUids();
        Set(main,"IncCtrl",MessagePackSerializer.Deserialize<RoleIncreaserCtrl>(MessagePackSerializer.Serialize(main.IncCtrl)));
        Check((bool)Call("Features.Effects.TraitCooldownPatches","Enabled"),"no cooldown survives native increaser serialization");
        main.RemoveEffect(ids);
        Check(!(bool)Call("Features.Effects.TraitCooldownPatches","Enabled") && npc.traitChangeCnt==12,"removing effect restores original cooldown record");
        Run(20,5418,1,0);
        Check(!(bool)Call("Features.Effects.TraitCooldownPatches","Enabled"),"invalid no-cooldown value cannot enable feature");
        var gated=CommonEvtMgr.GenEffector(new List<float>{20,5418,1,1},null,0,102);
        gated.SetRateType(10001); gated.Run(); Set(npc,"Relation",0);
        Check(!(bool)Call("Features.Effects.TraitCooldownPatches","Enabled"),"trait-origin no cooldown retains native relationship gate");
        Set(npc,"Relation",6);
        Check((bool)Call("Features.Effects.TraitCooldownPatches","Enabled"),"relationship recovery reactivates trait-origin no cooldown");
        main.RemoveEffect(gated.GetBaseIncreaserUids());

        string template=Path.Combine(outputDirectory,"role-template"); Directory.CreateDirectory(template);
        string sample="{\"roles\":[]}"; File.WriteAllText(Path.Combine(template,"RoleAvailabilityCfg.json"),sample);
        Call("Core.TemplateMaintenance","WriteRoleAvailabilityTemplate",template);
        string instructions=File.ReadAllText(Path.Combine(template,"RoleAvailability说明.txt"));
        Check(instructions.Contains("\"hiddenkzone\": true") && instructions.Contains("[23,3,-2,9001]") && instructions.Contains("留言") && instructions.Contains("点赞"),"generated template has JSON and every requested visibility scope");
        Check(File.ReadAllText(Path.Combine(template,"RoleAvailabilityCfg.json"))==sample,"template refresh preserves author JSON");
        using(var stream=up.GetManifestResourceStream("EC2BUnofficialPatch.Embedded.RoleAvailability.Readme.txt"))
        using(var reader=new StreamReader(stream)) Check(reader.ReadToEnd()==instructions,"generated instructions use the embedded canonical document");

        string directory=Path.Combine(outputDirectory,"visibility-mod"); Directory.CreateDirectory(directory);
        string file=Path.Combine(directory,"RoleAvailabilityCfg.json");
        File.WriteAllText(file,"{\"roles\":[{\"personId\":102,\"cond\":[[1,1,1,10000]],\"takeExam\":false,\"hiddenkzone\":true}]}");
        var root=Activator.CreateInstance(T("Workshop.ContentRoot"),All,null,new object[]{"qa-role",directory},null);
        var roots=Array.CreateInstance(root.GetType(),1);roots.SetValue(root,0);
        var catalog=Activator.CreateInstance(T("Workshop.ContentRootCatalog"),All,null,new object[]{roots},null);
        var service=Call("Services.RoleAvailabilityService","Load",catalog);
        Call(Availability,"Initialize",service);
        Check(!(bool)Call(Availability,"SocialAvailable",102),"unmet native cond hides social role");
        var visibility=new Harmony("qa.role-visibility");
        // RedpointMgr's static constructor allocates Unity GameObjects. Isolate only
        // the publication notification; retain native data/time/comment creation.
        visibility.Patch(AccessTools.Method(typeof(KZoneData),"Post"),transpiler:new HarmonyMethod(typeof(EffectExtensionCheck),nameof(IsolatePostNotification)));
        var services=Activator.CreateInstance(T("Core.PluginServices"),All,null,new object[]{catalog,null,null,null,null},null);
        var module=Activator.CreateInstance(T("Features.Mechanics.RoleAvailabilityModule"),true);
        module.GetType().GetMethod("Load").Invoke(module,new object[]{visibility,services});
        Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(KZonePageHomeView),"Refresh")).Transpilers.Any(p=>p.owner==visibility.Id),"home feed projection installs");
        Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(KZonePageHomeView),"HasMore")).Transpilers.Any(p=>p.owner==visibility.Id),"pagination uses same visibility projection");
        foreach(var type in new[]{typeof(KZonePageTalkView),typeof(KZonePageBlogView),typeof(KZonePagePhotoView)})
            Check(Harmony.GetPatchInfo(AccessTools.Method(type,type==typeof(KZonePageTalkView)?"Refresh":"SetData")).Owners.Contains(visibility.Id),"personal page hook installs: "+type.Name);
        Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(BaseView),"OnUpdateEventListeners")).Owners.Contains(visibility.Id),"photo page registers refresh through native view lifecycle");
        Run(22,999,2,102); Check((bool)Call(Availability,"SocialAvailable",102),"social show overrides unmet cond for followed role");
        Run(22,999,3,102); main.SetAttr(1,20000); Check(!(bool)Call(Availability,"SocialAvailable",102),"explicit hide survives later satisfied cond");
        Run(22,999,2,102); Check((bool)Call(Availability,"SocialAvailable",102),"latest social instruction wins");
        var people=Cfg.PersonCfgMap;
        typeof(Cfg).GetProperty("PersonCfgMap").SetValue(null,new Dictionary<int,PersonCfg>{[102]=people[102]});
        var social=new RelationData();
        Check(social.GetAllSocialNpcs(true).SequenceEqual(new[]{102}),"actual native social list includes explicitly shown followed role");
        Run(22,999,3,102);
        Check(social.GetAllSocialNpcs(true).Count==0,"actual native social list removes explicitly hidden role");
        Run(22,999,2,102); typeof(Cfg).GetProperty("PersonCfgMap").SetValue(null,people);
        Set(npc,"Relation",0); Check(!(bool)Call(Availability,"SocialAvailable",102),"show instruction does not manufacture a followed role"); Set(npc,"Relation",6);

        typeof(Cfg).GetProperty("KZoneContentCfgMap").SetValue(null,new Dictionary<int,KZoneContentCfg>{
            [301]=new KZoneContentCfg{id=301,role=102,thumbs=new List<List<int>>(),comments=new List<List<int>>(),imgs=new List<string>{"test/photo"}},
            [302]=new KZoneContentCfg{id=302,role=3},[303]=new KZoneContentCfg{id=303,role=102,thumbs=new List<List<int>>(),comments=new List<List<int>>()}});
        typeof(Cfg).GetProperty("KZoneCommentCfgMap").SetValue(null,new Dictionary<int,KZoneCommentCfg>{
            [9001]=new KZoneCommentCfg{id=9001,roles=new List<int>{102}},
            [9002]=new KZoneCommentCfg{id=9002,roles=new List<int>{3},parent=9001},
            [9003]=new KZoneCommentCfg{id=9003,roles=new List<int>{3}},
            [9004]=new KZoneCommentCfg{id=9004,roles=new List<int>{102}}});
        typeof(Cfg).GetProperty("KZoneMessageBoardCfgMap").SetValue(null,new Dictionary<int,KZoneMessageBoardCfg>{
            [501]=new KZoneMessageBoardCfg{id=501,roles=new List<int>{102,0}},
            [502]=new KZoneMessageBoardCfg{id=502,roles=new List<int>{3,0}},
            [503]=new KZoneMessageBoardCfg{id=503,roles=new List<int>{102,3}},
            [504]=new KZoneMessageBoardCfg{id=504,roles=new List<int>{3,0}},
            [505]=new KZoneMessageBoardCfg{id=505,roles=new List<int>{3,0}}});
        var post=new KZoneContentData{id=301,postTime=100,type=KZoneContentType.Talk,thumbs=new List<int>{102,3},customThumbCnt=8};
        var other=new KZoneContentData{id=302,postTime=300,type=KZoneContentType.Talk,thumbs=new List<int>{102,3},
            comments=new Dictionary<int,KZoneCommentData>{[9001]=new KZoneCommentData{id=9001,postTime=200},[9002]=new KZoneCommentData{id=9002,postTime=201},[9003]=new KZoneCommentData{id=9003,postTime=202}}};
        var zone=new KZoneData{datas=new Dictionary<int,KZoneContentData>{[301]=post,[302]=other},msgs=new Dictionary<int,List<KZoneMsgData>>{
            [0]=new List<KZoneMsgData>{new KZoneMsgData{id=501,idx=2},new KZoneMsgData{id=502,parentId=501},new KZoneMsgData{id=505,parentId=502},new KZoneMsgData{id=504,idx=1}},
            [3]=new List<KZoneMsgData>{new KZoneMsgData{id=503,idx=1}}}};
        model.kzoneData=zone;
        Check(!PostVisible(post) && PostVisible(other),"hiddenkzone filters author, independent of social show");
        Check(Visible(zone).Keys.SequenceEqual(new[]{302}),"home/photo/log projection excludes hidden post");
        Check(Comments(other).Keys.SequenceEqual(new[]{9003}),"hidden author comment and its other-author reply are suppressed");
        Check(Likes(other).SequenceEqual(new[]{3}),"hidden author's like on someone else's post is suppressed");
        Check(zone.GetMsgs(0).Count==1 && zone.GetMsgs(0)[0].parentId==0 && zone.GetMsgs(3).Count==0,"native message retrieval filters author across boards and hides replies with hidden parent");
        Check(zone.datas.Count==2 && other.comments.Count==3 && other.thumbs.Count==2 && zone.msgs[0].Count==4,"all original published records remain stored");
        Run(23,3,1,301); Check(PostVisible(post) && post.postTime==100,"single reveal overrides hiddenkzone without changing original time");
        Run(23,3,2,9001); Check(Comments(other).Count==3,"single comment reveal restores visible replies");
        Run(23,3,-2,9001); Check(Comments(other).Keys.SequenceEqual(new[]{9003}),"negative two hides comment, not publication");
        Run(23,3,999,102);
        Check(PostVisible(post) && Comments(other).Count==3 && Likes(other).Count==2 && zone.GetMsgs(0).Count==4 && zone.GetMsgs(3).Count==1,"role reveal covers posts, foreign comments, likes, boards and nested replies");
        Run(23,3,-1,301); Check(!PostVisible(post),"single hide after bulk show takes precedence");
        Run(23,3,999,102); Check(PostVisible(post),"later bulk show overrides earlier single hide");
        Run(23,3,1,303); Check(zone.datas.Count==2,"unpublished reveal cannot create content");
        zone.Post(303); var future=zone.datas[303];
        Check(future.postTime>0,"native publisher still creates and timestamps hidden author's future post");
        Check(!PostVisible(future),"future post still follows hiddenkzone after role reveal");
        other.comments[9004]=new KZoneCommentData{id=9004,postTime=500};
        Check(!Comments(other).ContainsKey(9004),"future comment does not inherit previous bulk reveal");
        var sorted=Visible(zone).Values.ToList(); sorted.Sort(KZoneCommon.SortKZoneContent);
        Check(sorted.Select(p=>p.id).SequenceEqual(new[]{302,301}),"revealed older post returns to original chronological position");
        Check(KZoneCommon.SortKZoneContent(post,new KZoneContentData{id=304,postTime=100})<0,"equal timestamps have stable order when items are hidden and restored");
        Run(23,3,-999,102);
        Check(!PostVisible(post) && Comments(other).Keys.SequenceEqual(new[]{9003}) && Likes(other).SequenceEqual(new[]{3}) && zone.GetMsgs(3).Count==0,"role hide covers all existing authored scope");
        Check(other.comments[9001].postTime==200 && post.Imgs.Count==1,"visibility preserves comments' timestamps and image attachment data");
        byte[] saved=MessagePackSerializer.Serialize(main.IncCtrl);
        Set(main,"IncCtrl",MessagePackSerializer.Deserialize<RoleIncreaserCtrl>(saved));
        Check(!PostVisible(post) && (bool)Call(Availability,"SocialAvailable",102),"native serialization preserves independent space and social decisions");
        File.WriteAllText(file,"{\"roles\":[{\"personId\":102,\"cond\":[],\"takeExam\":false,\"hiddenkzone\":false}]}");
        Call(Availability,"Initialize",Call("Services.RoleAvailabilityService","Load",catalog));
        Cfg.KZoneContentCfgMap[304]=new KZoneContentCfg{id=304,role=102};
        Check(!PostVisible(post) && PostVisible(future) && PostVisible(new KZoneContentData{id=304,postTime=600}),"JSON reload preserves decisions; bulk hide does not freeze items that were already hidden");
        Reset(); Check(PostVisible(post),"new save does not inherit visibility overrides");
        visibility.UnpatchSelf();
        Check(zone.GetMsgs(0).Count==4,"disabling module restores native message list without deleting records");
    }
}
