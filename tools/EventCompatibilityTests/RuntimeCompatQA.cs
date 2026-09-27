using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using Config;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using Newtonsoft.Json;
using Sdk;
using Plugin = EC2BUnofficialPatch.Services.EventIdCompatibilityApi;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
using View.Main;
using View.Evt;

[BepInPlugin("local.studentage.eventcompat.qa", "EventCompat Isolated QA", "0.1.0")]
[BepInDependency("sa.EC2B.UnofficialPatch")]
public sealed class CompatQaPlugin : BaseUnityPlugin
{
    void Awake()
    {
        if(!File.Exists(Path.Combine(Paths.GameRootPath,".event-compat-qa")))throw new Exception("Refuse non-isolated player");
        var host=new GameObject("EventCompatQA");DontDestroyOnLoad(host);host.hideFlags=HideFlags.HideAndDontSave;host.AddComponent<CompatQaDriver>().Initialize(Logger);
    }
}
public sealed class CompatQaDriver : MonoBehaviour
{
    const int E=7700001,F=7700002,D=7700003,T=7700101,O=7700201;
    const ulong A=19008101UL,B=19008102UL;
    string root,output;ManualLogSource log;List<string> checks=new List<string>();
    static string ModPath(ulong id)=>Path.Combine(Paths.GameRootPath,"fixture-mod-"+id);
    static bool PathOverride(ulong id,ref string __result){__result=ModPath(id);return false;}
    static Exception TraceLoadError(Exception __exception)
    {
        if(__exception!=null)File.AppendAllText(Path.Combine(Paths.GameRootPath,"results/load-exception.txt"),__exception.ToString()+Environment.NewLine);
        return __exception;
    }
    static bool SkipWarning()=>false;
    static bool NoGuide()=>false;
    public void Initialize(ManualLogSource l){root=Paths.GameRootPath;output=Path.Combine(root,"results");Directory.CreateDirectory(output);log=l;Application.runInBackground=true;}
    void Check(bool ok,string text){if(!ok)throw new Exception(text);checks.Add(text);File.WriteAllLines(Path.Combine(output,"checks.txt"),checks);log.LogInfo("QA_PASS "+text);}
    IEnumerator Until(Func<bool> predicate,float seconds,string text){float end=Time.realtimeSinceStartup+seconds;while(!predicate()&&Time.realtimeSinceStartup<end)yield return null;Check(predicate(),text);}
    static IEnumerator Await(UniTask t){var a=t.GetAwaiter();while(!a.IsCompleted)yield return null;a.GetResult();}
    IEnumerator Shot(string name){yield return new WaitForEndOfFrame();var tex=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(output,name+".png"),tex.EncodeToPNG());Destroy(tex);}
    IEnumerator Start()
    {
        log.LogInfo("QA_DRIVER_START");
        var stack=new Stack<IEnumerator>();stack.Push(Run());bool failed=false;
        while(stack.Count>0){bool more=false;object next=null;try{more=stack.Peek().MoveNext();if(more)next=stack.Peek().Current;}catch(Exception e){File.WriteAllText(Path.Combine(output,"failed.txt"),e.ToString());log.LogError(e);failed=true;}
            if(failed)break;if(!more){stack.Pop();continue;}if(next is IEnumerator){stack.Push((IEnumerator)next);continue;}yield return next;}
        yield return Shot(failed?"failure":"complete");if(!failed)File.WriteAllText(Path.Combine(output,"success.txt"),"EVENT_COMPAT_RUNTIME_OK "+checks.Count);yield return new WaitForSecondsRealtime(1);Application.Quit();
    }
    void Fixtures()
    {
        var template=Cfg.TalkCfgMap.Values.First(t=>t.bg>0&&t.roleIds!=null&&t.roleIds.Count>0&&!string.IsNullOrEmpty(t.content));
        foreach(ulong mod in new[]{A,B})
        {
            string dir=Path.Combine(ModPath(mod),"Cfgs/zh-cn");Directory.CreateDirectory(dir);string label=mod==A?"甲":"乙";
            File.WriteAllText(Path.Combine(ModPath(mod),"manifest.json"),"{\"title\":\"兼容测试"+label+"\"}");
            Func<int,int,int,List<List<double>>,EvtCfg> evt=(id,type,value,cond)=>new EvtCfg{id=id,type=type,title="兼容测试"+label+" "+id,rate=1,maxcount=id==1?100000:1,condition=cond,effect=new List<List<float>>{new List<float>{50,2,id,0,value}}};
            var evts=new Dictionary<int,EvtCfg>{[E]=evt(E,777,mod==A?11:22,new List<List<double>>()),[F]=evt(F,778,mod==A?33:44,new List<List<double>>{new List<double>{3,1,E}}),[1]=evt(1,777,mod==A?123:456,new List<List<double>>()),[D]=new EvtCfg{id=D,type=779,title="对白测试"+label,rate=1,maxcount=1,talkId=new List<int>{T}}};
            var talk=new TalkCfg{id=T,bg=template.bg,roleIds=template.roleIds,roleName=template.roleName,content="兼容测试"+label+"：两个模组使用相同事件、对白和选项 ID，各自剧情应独立触发。",option=new List<int>{O}};
            var option=new OptionCfg{id=O,content="记录模组"+label+"的选项",effect=new List<List<float>>{new List<float>{50,2,E,1,mod==A?7:9}}};
            File.WriteAllText(Path.Combine(dir,"EvtCfg.json"),JsonConvert.SerializeObject(evts));
            File.WriteAllText(Path.Combine(dir,"TalkCfg.json"),JsonConvert.SerializeObject(new Dictionary<int,TalkCfg>{{T,talk}}));
            File.WriteAllText(Path.Combine(dir,"OptionCfg.json"),JsonConvert.SerializeObject(new Dictionary<int,OptionCfg>{{O,option}}));
            File.WriteAllText(Path.Combine(dir,"EvtTypeCfg.json"),JsonConvert.SerializeObject(new Dictionary<int,EvtTypeCfg>{{777,new EvtTypeCfg{id=777,name="QA首段",emptyIsTrue=1,type=new List<int>{1}}},{778,new EvtTypeCfg{id=778,name="QA后续",emptyIsTrue=1,type=new List<int>{1}}},{779,new EvtTypeCfg{id=779,name="QA对白",emptyIsTrue=1,type=new List<int>{1}}}}));
        }
    }
    IEnumerator Run()
    {
        yield return Until(()=>{var mgr=AccessTools.Field(typeof(UIMgr),"ins").GetValue(null);return mgr!=null&&AccessTools.Field(typeof(UIMgr),"viewDict").GetValue(mgr)!=null;},90,"native UI ready");
        yield return Until(()=>UIMgr.IsViewOpened<EntryView>(),90,"native entry loaded");
        Check(PathDefine.SAVE_PATH.Replace('\\','/').StartsWith(root.Replace('\\','/')+"/data/"),"managed saves isolated");
        Check(Application.companyName=="EventcQA0"&&Application.productName=="EventcQA00","native preferences isolated");
        var h=new Harmony("eventcompat.qa.fixtures");h.Patch(AccessTools.Method(typeof(ProfileMgr),"ValidateModList"),prefix:new HarmonyMethod(typeof(CompatQaDriver),nameof(SkipWarning)));
        h.Patch(AccessTools.Method(typeof(MainView),"CheckGuide"),prefix:new HarmonyMethod(typeof(CompatQaDriver),nameof(NoGuide)));
        bool done=false,loaded=false;SaveMgrEx.LoadAynsc(root,"fixture.save",16,ok=>{done=true;loaded=ok;});yield return Until(()=>done,30,"synthetic fixture load finished");Check(loaded,"synthetic fixture loaded");
        Fixtures();
        var overlayType=typeof(Plugin).Assembly.GetType("EC2BUnofficialPatch.Features.Mechanics.ModCfgOverridePatches");
        var roots=(HashSet<string>)AccessTools.Field(overlayType,"WorkshopRoots").GetValue(null);
        roots.Add(ModPath(A));roots.Add(ModPath(B));
        foreach(ulong id in new[]{A,B})
        {
            string enhanced=Path.Combine(ModPath(id),"EC2BUnofficialPatch/LoveDraw");Directory.CreateDirectory(enhanced);
            File.WriteAllText(Path.Combine(enhanced,"LoveDrawCfg.json"),"{\"7700301\":{\"id\":7700301,\"talkId\":["+T+"]}}");
        }
        var sourceBefore=Directory.GetFiles(ModPath(A),"*.json",SearchOption.AllDirectories).Concat(Directory.GetFiles(ModPath(B),"*.json",SearchOption.AllDirectories)).ToDictionary(p=>p,p=>File.ReadAllText(p));
        bool reverse=File.Exists(Path.Combine(root,"reverse-order.txt"));var order=reverse?new[]{B,A}:new[]{A,B};
        var ctrl=Singleton<ModCtrl>.Ins;ctrl.activeMods=new List<ulong>(order);
        Check(!Plugin.Applied,"no prior mod plan in clean isolated boot");
        h.Patch(AccessTools.Method(typeof(Plugin).Assembly.GetType("EC2BUnofficialPatch.Services.EventCompatibility.EventIdCompatibilityService"),"ModDirectory"),prefix:new HarmonyMethod(typeof(CompatQaDriver),nameof(PathOverride)));
        var hotReload=typeof(Plugin).Assembly.GetType("EC2BUnofficialPatch.Services.GameModCfgHotReload");
        AccessTools.Field(hotReload,"_baseMaps").SetValue(null,null);
        string mode=File.Exists(Path.Combine(root,"qa-mode.txt"))?File.ReadAllText(Path.Combine(root,"qa-mode.txt")).Trim():"enabled";
        Type standalone=AppDomain.CurrentDomain.GetAssemblies().Select(assembly=>assembly.GetType("StudentAge.EventCompat.Plugin",false)).FirstOrDefault(type=>type!=null);
        if(mode=="standalone")
            h.Patch(AccessTools.Method(standalone,"ModDirectory"),prefix:new HarmonyMethod(typeof(CompatQaDriver),nameof(PathOverride)));
        var loader=AccessTools.Method(typeof(ModCtrl),"LoadModCfgs");
        foreach(ulong id in order)yield return Await((UniTask)loader.Invoke(ctrl,new object[]{id,Path.Combine(ModPath(id),"Cfgs/zh-cn")}));
        yield return Await(ctrl.MergeCfgsAsync());
        if(mode=="disabled"||mode=="standalone")
        {
            Check(!Plugin.Applied,"UP mapping inactive in "+mode+" mode");
            string file=Path.Combine(ModPath(A),"Cfgs/zh-cn/EvtCfg.json");var cfg=JObject.Parse(File.ReadAllText(file));cfg[E.ToString()]["title"]="关闭测试";File.WriteAllText(file,cfg.ToString());
            var reloadResult=(EC2BUnofficialPatch.Core.JsonHotReloadResult)AccessTools.Method(hotReload,"Reload").Invoke(null,null);
            if(mode=="disabled")
            {
                Check(Cfg.EvtCfgMap.Values.Count(row=>row.id==E)==1&&Cfg.EvtCfgMap[E].title=="关闭测试","disabled option retains native first-wins and ordinary F9");
                Check(reloadResult.Success&&!reloadResult.Skipped,"disabled compatibility does not block ordinary CFG reload");
            }
            else
            {
                Check((bool)standalone.GetProperty("Applied").GetValue(null,null),"standalone plugin owns the sole mapping pass");
                int alias=(int)standalone.GetMethod("Resolve").Invoke(null,new object[]{B,"EvtCfg",E});
                Check(alias!=E&&Cfg.EvtCfgMap.ContainsKey(alias)&&Cfg.EvtCfgMap[E].title.Contains("甲"),"standalone and UP coexist without duplicate remapping");
                Check(!reloadResult.Success&&Cfg.EvtCfgMap[E].title.Contains("甲"),"UP rejects F9 that would discard standalone mappings");
            }
            yield break;
        }
        Check(Plugin.Applied,"compatibility plan applied through actual native mod loader");
        int eb=Plugin.Resolve(B,"EvtCfg",E),fb=Plugin.Resolve(B,"EvtCfg",F),db=Plugin.Resolve(B,"EvtCfg",D),tb=Plugin.Resolve(B,"TalkCfg",T),ob=Plugin.Resolve(B,"OptionCfg",O);
        Check(eb!=E&&tb!=T&&ob!=O,"colliding event talk and option have separate runtime IDs");
        Check(Cfg.EvtCfgMap[E].title.Contains("甲")&&Cfg.EvtCfgMap[eb].title.Contains("乙"),"native event dictionary retains both mods");
        Check(Cfg.EvtCfgMap[1].title.Contains(reverse?"乙":"甲")&&Plugin.Resolve(A,"EvtCfg",1)==1&&Plugin.Resolve(B,"EvtCfg",1)==1,"native event override is a single first-wins slot");
        Check(Cfg.EvtCfgMap[db].talkId[0]==tb&&Cfg.TalkCfgMap[tb].option[0]==ob,"native deserialization retains remapped story chain");
        Check(Cfg.LoveDrawCfgMap[7700301].talkId[0]==(reverse?tb:T),"UP enhanced CFG injection references the winning mod dialogue");
        Check(sourceBefore.All(p=>File.ReadAllText(p.Key)==p.Value),"original source JSON unchanged");
        var profile=(ProfileModel)AccessTools.Field(typeof(ProfileMgr),"model").GetValue(Singleton<ProfileMgr>.Ins);profile.modList=new List<ulong>{A,B};
        var game=AccessTools.Field(typeof(Game),"ins").GetValue(null);AccessTools.Field(typeof(Game),"<state>k__BackingField").SetValue(game,GameState.Running);
        AccessTools.Field(typeof(RoundMgr),"<RoundState>k__BackingField").SetValue(Singleton<RoundMgr>.Ins,RoundState.FinishPrepare);
        UIMgr.CloseAllView();UIMgr.OpenView<MainView>();UIMgr.OpenView<TopView>();yield return Until(()=>UIMgr.IsViewOpened<MainView>(),25,"in-game UI ready");
        yield return Until(()=>UIMgr.GetView<MainView>().isViewReady&&UIMgr.GetView<TopView>().isViewReady,20,"main and top view transitions settled");
        yield return new WaitForSecondsRealtime(3);
        var mgr=Singleton<CommonEvtMgr>.Ins;AccessTools.Method(typeof(CommonEvtMgr),"InitEvtCfg").Invoke(mgr,null);
        mgr.RefreshEvt(777);Check(mgr.GetEvtCntInQueue(777)==3,"natural refresh queues both additions and one native override");
        var dq=AccessTools.Method(typeof(CommonEvtMgr),"DequeueEvt");var queued=new List<int>();
        while(mgr.GetEvtCntInQueue(777)>0)queued.Add(((ValueTuple<CommonEvtMgr.EvtQueueType,int>)dq.Invoke(mgr,new object[]{777})).Item2);
        Check(queued.Distinct().Count()==3&&queued.Contains(E)&&queued.Contains(eb)&&queued.Count(x=>x==1)==1,"queue has no duplicate original or lost mod event");
        Check(!CommonEvtMgr.IsMatchCondition(Cfg.EvtCfgMap[F].condition,true)&&!CommonEvtMgr.IsMatchCondition(Cfg.EvtCfgMap[fb].condition,true),"both followup chains initially locked");
        mgr.ShowEvent(E);Check(mgr.GetEvtSaveData(E,0)==11&&!mgr.HasEventHappended(eb),"A trigger does not mark B as happened");
        Check(CommonEvtMgr.IsMatchCondition(Cfg.EvtCfgMap[F].condition,true)&&!CommonEvtMgr.IsMatchCondition(Cfg.EvtCfgMap[fb].condition,true),"A history unlocks only A followup");
        mgr.RefreshEvt(778);Check(mgr.GetEvtCntInQueue(778)==1,"native followup refresh sees only eligible mod");dq.Invoke(mgr,new object[]{778});
        bool saved=false;done=false;Singleton<FuncMgr>.Ins.GetMapData().recordMapId=1;
        Game.ManualSaveGame(1,"事件兼容隔离存档",null,ok=>{saved=ok;done=true;});yield return Until(()=>done,30,"manual save finished");Check(saved,"native manual save succeeds");
        string save=SaveMgr.GetPref("LatestSaveKey","");Check(File.Exists(Path.Combine(PathDefine.SAVE_PATH,save)),"save stored only in isolated directory");
        mgr.ShowEvent(eb);Check(mgr.GetEvtSaveData(eb,0)==22&&mgr.GetEvtSaveData(E,0)==11,"B effect writes only B event variables");
        foreach(var nested in typeof(Game).GetNestedTypes(System.Reflection.BindingFlags.NonPublic))
            foreach(var method in AccessTools.GetDeclaredMethods(nested).Where(method=>method.Name.Contains("<LoadGame>")&&method.GetParameters().Length==1&&method.GetParameters()[0].ParameterType==typeof(string)))
                h.Patch(method,finalizer:new HarmonyMethod(typeof(CompatQaDriver),nameof(TraceLoadError)));
        Game.LoadGame(save);yield return Until(()=>mgr.GetEvtSaveData(eb,0)==0&&mgr.HasEventHappended(E)&&!mgr.HasEventHappended(eb),30,"native load restores independent A and B histories");yield return new WaitForSecondsRealtime(1);
        Check(Plugin.Resolve(B,"EvtCfg",E)==eb,"mapping persists across load");
        mgr.ShowEvent(eb);mgr.ShowEvent(1);Check(mgr.GetEvtSaveData(1,0)==(reverse?456:123),"only mod override of original executes");
        Check(CommonEvtMgr.IsMatchCondition(Cfg.EvtCfgMap[fb].condition,true),"B followup unlocks after B trigger");
        mgr.RefreshEvt(777);Check(mgr.GetEvtCntInQueue(777)==1,"one-shot A and B independently suppressed after both happen");while(mgr.GetEvtCntInQueue(777)>0)dq.Invoke(mgr,new object[]{777});
        foreach(ulong id in new[]{A,B})
        {
            string label=id==A?"甲":"乙";int eventId=Plugin.Resolve(id,"EvtCfg",D),talkId=Plugin.Resolve(id,"TalkCfg",T),optionId=Plugin.Resolve(id,"OptionCfg",O);
            mgr.ShowEvent(eventId);yield return Until(()=>UIMgr.IsViewOpened<NewTalkView>(),20,"native dialogue opens for "+label);yield return new WaitForSecondsRealtime(2);
            yield return Until(()=>UIMgr.GetView<NewTalkView>().gameObject.GetComponentsInChildren<TMPro.TMP_Text>(true).Any(t=>t.text.Contains("兼容测试"+label)&&t.text.Contains("独立触发")),15,"rendered dialogue belongs to "+label);yield return Shot("dialogue-"+id);
            var options=mgr.GetTalkOptions(talkId);Check(options.Count==1&&options[0].id==optionId&&options[0].cfg.content.Contains(label),"native option belongs to "+label);
            options[0].callback=()=>{};mgr.SelectOption(options[0]);yield return new WaitForSecondsRealtime(.8f);
            Check(mgr.HasOptionSelected(optionId),"native option history written for "+label);
        }
        Check(mgr.GetEvtSaveData(E,1)==7&&mgr.GetEvtSaveData(eb,1)==9,"both option outcomes remain isolated");
        Check(File.Exists(Path.Combine(Paths.ConfigPath,"StudentAge.EventCompat/id-map.json")),"persistent ID registry saved");
        // Same provider invoked by F9: accepted documents, enhanced CFG and natural-event index.
        string eventFile=Path.Combine(ModPath(B),"Cfgs/zh-cn/EvtCfg.json");
        var document=JObject.Parse(File.ReadAllText(eventFile));
        document[E.ToString()]["title"]="乙热重载";
        document["7700009"]=JObject.FromObject(new EvtCfg{id=7700009,type=778,title="热重载新增",rate=1,maxcount=1});
        File.WriteAllText(eventFile,document.ToString());
        var reload=AccessTools.Method(hotReload,"Reload");
        var result=(EC2BUnofficialPatch.Core.JsonHotReloadResult)reload.Invoke(null,null);
        Check(result.Success&&!result.Skipped,"UP F9 ordinary CFG provider applied changes: "+result.Message);
        Check(Plugin.Resolve(B,"EvtCfg",E)==eb&&Cfg.EvtCfgMap[eb].title=="乙热重载"&&Cfg.EvtCfgMap[E].title.Contains("甲"),"F9 keeps alias identity and both events");
        Check(Cfg.EvtCfgMap[1].title.Contains(reverse?"乙":"甲"),"F9 preserves native override winner");
        Check(mgr.HasEventHappended(E)&&mgr.HasEventHappended(eb)&&mgr.GetEvtSaveData(eb,1)==9,"F9 retains event histories and effects");
        mgr.RefreshEvt(778);var afterReload=new List<int>();while(mgr.GetEvtCntInQueue(778)>0)afterReload.Add(((ValueTuple<CommonEvtMgr.EvtQueueType,int>)dq.Invoke(mgr,new object[]{778})).Item2);
        Check(afterReload.Contains(7700009),"F9 rebuilds natural event index for new rows");
        string talkFile=Path.Combine(ModPath(B),"Cfgs/zh-cn/TalkCfg.json");string validTalk=File.ReadAllText(talkFile);File.WriteAllText(talkFile,"broken json");
        document[E.ToString()]["title"]="乙有效变更";File.WriteAllText(eventFile,document.ToString());
        result=(EC2BUnofficialPatch.Core.JsonHotReloadResult)reload.Invoke(null,null);
        Check(result.Success&&Cfg.EvtCfgMap[eb].title=="乙有效变更"&&Cfg.TalkCfgMap[tb].content.Contains("乙"),"F9 retains last-good broken file while applying valid remapped changes");
        File.WriteAllText(talkFile,validTalk);
        string registryFile=Path.Combine(Paths.ConfigPath,"StudentAge.EventCompat/id-map.json"),registry=File.ReadAllText(registryFile);
        document[eb.ToString()]=JObject.FromObject(new EvtCfg{id=eb,type=778,title="冲突",rate=1});File.WriteAllText(eventFile,document.ToString());
        result=(EC2BUnofficialPatch.Core.JsonHotReloadResult)reload.Invoke(null,null);
        Check(!result.Success&&Cfg.EvtCfgMap[eb].title=="乙有效变更"&&File.ReadAllText(registryFile)==registry,"F9 alias collision rolls back tables and durable mapping");
        document.Remove(eb.ToString());File.WriteAllText(eventFile,document.ToString());
        string firstFile=Path.Combine(ModPath(A),"Cfgs/zh-cn/EvtCfg.json");var first=JObject.Parse(File.ReadAllText(firstFile));
        first["7700009"]=JObject.FromObject(new EvtCfg{id=7700009,type=778,title="失败事务中的新撞号",rate=1});File.WriteAllText(firstFile,first.ToString());
        string typeFile=Path.Combine(ModPath(B),"Cfgs/zh-cn/EvtTypeCfg.json");var types=JObject.Parse(File.ReadAllText(typeFile));types["780"]=JObject.FromObject(new EvtTypeCfg{id=780,name="无效类型索引",type=null});File.WriteAllText(typeFile,types.ToString());
        result=(EC2BUnofficialPatch.Core.JsonHotReloadResult)reload.Invoke(null,null);
        Check(!result.Success&&!Cfg.EvtTypeCfgMap.ContainsKey(780)&&File.ReadAllText(registryFile)==registry&&Plugin.Resolve(A,"EvtCfg",7700009)==7700009,"F9 index failure rolls back maps and uncommitted aliases");
        mgr.RefreshEvt(778);var afterFailure=new List<int>();while(mgr.GetEvtCntInQueue(778)>0)afterFailure.Add(((ValueTuple<CommonEvtMgr.EvtQueueType,int>)dq.Invoke(mgr,new object[]{778})).Item2);
        Check(afterFailure.Contains(7700009),"F9 rollback restores the prior natural event index");
        File.WriteAllText(Path.Combine(output,"runtime-ids.json"),JsonConvert.SerializeObject(new { E,eb,F,fb,D,db,T,tb,O,ob },Formatting.Indented));
    }
}
