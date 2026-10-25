using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using HarmonyLib;
using Newtonsoft.Json;

internal static class ReloadInputCheck
{
    static Assembly up;
    static List<string> passed = new List<string>();
    static Type T(string name) => up.GetType("EC2BUnofficialPatch." + name, true);
    static object Call(string type, string method, params object[] args) => AccessTools.Method(T(type), method).Invoke(null, args);
    static void Set(string type, string field, object value) => AccessTools.Field(T(type), field).SetValue(null, value);
    static object Get(string type, string field) => AccessTools.Field(T(type), field).GetValue(null);
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); passed.Add(label); }
    static bool Flag(object value, string name) => (bool)value.GetType().GetProperty(name).GetValue(value);
    static bool Same(object a, object b) => (bool)AccessTools.Method(a.GetType(), "SameAs").Invoke(a, new[] {b});
    static object Snapshot(params string[] paths) => Call("Core.ReloadInputSnapshot", "Capture", paths, true);
    public static int Main(string[] args)
    {
        var roots = new[] {Path.Combine(args[4], "clr-runtime"), Path.GetDirectoryName(args[0]), Path.Combine(args[1], "StudentAge_Data/Managed"), args[2]};
        AppDomain.CurrentDomain.AssemblyResolve += (_, request) => {
            string file = new AssemblyName(request.Name).Name + ".dll";
            string path = roots.Select(r => Path.Combine(r,file)).FirstOrDefault(File.Exists);
            return path == null ? null : Assembly.LoadFrom(path);
        };
        Assembly.LoadFrom(Path.Combine(args[4], "clr-runtime/UnityEngine.CoreModule.dll"));
        try { return Run(args); } catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Run(string[] args)
    {
        up = Assembly.LoadFrom(args[0]);
        string fixture = Path.Combine(args[4], Guid.NewGuid().ToString("N"));
        string game = Path.Combine(fixture, "steamapps/common/StudentAge"); Directory.CreateDirectory(game);
        var init = AccessTools.Method(typeof(Paths), "SetExecutablePath");
        var initArgs = init.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : null).ToArray();
        initArgs[0] = Path.Combine(game,"StudentAge.exe"); init.Invoke(null,initArgs);
        string mod = Path.Combine(fixture,"steamapps/workshop/content/1991040/3672553808");
        string map = Path.Combine(mod,"MapExtend"), badminton = Path.Combine(mod,"custombadminton");
        Directory.CreateDirectory(map); Directory.CreateDirectory(badminton);
        string locations = Path.Combine(map,"MapLocationscfg.json"), scenes = Path.Combine(map,"SceneObjectscfg.json");
        string worlds = Path.Combine(map,"Worldmapscfg.json"), bird = Path.Combine(badminton,"custombadmintoncfg.json");
        File.WriteAllText(locations,"{\"schema\":2,\"locations\":[]}");
        File.WriteAllText(scenes,"{\"schema\":2,\"objects\":[]}");
        File.WriteAllText(worlds,"{\"schema\":1,\"worlds\":[]}"); File.WriteAllText(bird,"{}");
        var catalog = Call("Workshop.ContentRootCatalog","Discover");
        var contentRoots = AccessTools.Property(catalog.GetType(),"Roots").GetValue(catalog);
        const string bp="Features.Mechanics.CustomBadmintonPatches", mp="Features.Mechanics.MapExtend.MapLocationsPatches", sp="Features.Mechanics.MapExtend.SceneObjectsRuntime";
        var br = Call("Features.Mechanics.CustomBadmintonRegistry","Load",contentRoots);
        Call(bp,"Initialize",br,catalog); Call(mp,"Initialize",catalog);
        var sr = Call("Features.Mechanics.MapExtend.SceneObjectRegistry","Load",contentRoots);
        Set(sp,"Registry",sr); Set(sp,"_inputs",Call(sp,"CaptureInputs",contentRoots));
        for(int i=0;i<3;i++) foreach(string provider in new[]{bp,mp,sp})
            Check(Flag(Call(provider,"Reload"),"Skipped"),"unchanged provider skips, including first F9: "+provider+"/"+i);
        Check(ReferenceEquals(br,Get(bp,"_registry")) && ReferenceEquals(sr,Get(sp,"Registry")),"no-op preserves live registry references");
        Check((int)Get(sp,"Revision")==0,"no-op leaves scene revision unchanged");
        Directory.CreateDirectory(Path.Combine(mod,"Cfgs/zh-cn"));
        File.WriteAllText(Path.Combine(mod,"Cfgs/zh-cn/TraitsCfg.json"),"{}");
        foreach(string provider in new[]{bp,mp,sp}) Check(Flag(Call(provider,"Reload"),"Skipped"),"ordinary TraitsCfg change does not refresh "+provider);
        File.SetLastWriteTimeUtc(bird,DateTime.UtcNow.AddMinutes(1));
        Check(Flag(Call(bp,"Reload"),"Skipped"),"timestamp-only save skips");
        File.WriteAllText(bird,"{ }");
        Check(!Flag(Call(bp,"Reload"),"Skipped"),"changed badminton file reloads");
        Check(Flag(Call(bp,"Reload"),"Skipped"),"successful badminton reload commits baseline");
        File.WriteAllBytes(Path.Combine(badminton,"model.png"),new byte[]{1,2});
        Check(!Flag(Call(bp,"Reload"),"Skipped"),"badminton texture addition reloads");
        File.WriteAllText(worlds,"{\"schema\":1,\"worlds\":[] } ");
        var mapResult=Call(mp,"Reload");
        Check(Flag(mapResult,"Success") && !Flag(mapResult,"Skipped"),"world configuration change executes actual map reload");
        Check(Flag(Call(mp,"Reload"),"Skipped"),"successful map reload commits baseline");
        var baseMap=Call(mp,"CaptureInputs",contentRoots); var baseScene=Call(sp,"CaptureInputs",contentRoots);
        File.WriteAllText(scenes,"{\"schema\":2,\"objects\":[],\"image\":\"object.png\"}");
        Check(Same(baseMap,Call(mp,"CaptureInputs",contentRoots)),"scene-only edit does not invalidate map provider");
        var imageBase=Call(sp,"CaptureInputs",contentRoots);
        Check(!Same(baseScene,imageBase),"scene JSON edit detected");
        string png=Path.Combine(map,"object.png"); File.WriteAllBytes(png,new byte[]{1,2});
        var imageAdded=Call(sp,"CaptureInputs",contentRoots);
        Check(!Same(imageBase,imageAdded),"referenced image creation detected");
        DateTime time=File.GetLastWriteTimeUtc(png); File.WriteAllBytes(png,new byte[]{3,4}); File.SetLastWriteTimeUtc(png,time);
        Check(!Same(imageAdded,Call(sp,"CaptureInputs",contentRoots)),"same-length same-timestamp image edit detected");
        Check(Same(baseMap,Call(mp,"CaptureInputs",contentRoots)),"scene image edit does not invalidate map provider");
        File.WriteAllText(scenes,"{broken");
        for(int i=0;i<2;i++) Check(!Flag(Call(sp,"Reload"),"Success"),"invalid JSON remains retryable "+i);
        Check(ReferenceEquals(sr,Get(sp,"Registry")) && (int)Get(sp,"Revision")==0,"parse failure preserves scene registry and revision");
        File.WriteAllText(scenes,"{\"schema\":2,\"objects\":[]}");
        Check(Flag(Call(sp,"Reload"),"Skipped"),"restoring original file matches last successful baseline");
        var beforeDelete=Snapshot(locations,worlds); File.Delete(worlds);
        Check(!Same(beforeDelete,Snapshot(locations,worlds)),"configuration deletion detected");
        Check(Same(Snapshot(null,locations),Snapshot(locations)),"missing discovery entries are safe");
        File.WriteAllText(Path.Combine(args[4],"result.json"),JsonConvert.SerializeObject(new {checks=passed.Count,passed=true,cases=passed},Formatting.Indented));
        Console.WriteLine("PASS: "+passed.Count+" reload checks; actual no-op callbacks exercised, Unity scene rendering excluded."); return 0;
    }
}
