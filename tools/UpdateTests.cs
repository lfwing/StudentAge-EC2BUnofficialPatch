using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using EC2BUnofficialPatch.Core;
using EC2BUnofficialPatch.Core.Updates;

internal static class UpdateChecks {
 static int count;
 static void Check(bool value,string name){if(!value)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
 static void Reject(Action call,string name){try{call();}catch(InvalidDataException){Check(true,name);return;}throw new Exception("Accepted: "+name);}
 static object Call(Type type,string name,params object[] args)=>type.GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);
 static UpdateManifest Manifest(string layout)=>new UpdateManifest {schema=1,layout=layout,version="1.0.26",channel="stable",assetName="EC2BUnofficialPatch.dll",size=1024,sha256=new string('a',64),downloadUrls=new List<string>{"https://example.org/UP.dll"}};
 static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
 static int Main(){
  foreach(var layout in new[]{"merged","split"}){
   UpdateService.ValidateManifestForLayout(Manifest(layout),layout);Check(true,layout+" accepts matching schema 1");
   Reject(()=>UpdateService.ValidateManifestForLayout(Manifest(layout=="merged"?"split":"merged"),layout),layout+" rejects opposite layout");
   var m=Manifest(layout);m.layout=null;Reject(()=>UpdateService.ValidateManifestForLayout(m,layout),layout+" rejects unmarked old UP feed");
   m=Manifest(layout);m.schema=2;Reject(()=>UpdateService.ValidateManifestForLayout(m,layout),layout+" rejects removed schema 2");
  }
  foreach(var change in new Action<UpdateManifest>[] {m=>m.size=0,m=>m.size=32L*1024*1024+1,m=>m.sha256="bad",m=>m.assetName="LFBetterAudio.dll",m=>m.downloadUrls.Clear(),m=>m.downloadUrls.Add("http://example.org/UP.dll"),m=>m.version="bad",m=>m.channel="beta"}){
   var m=Manifest("split");change(m);Reject(()=>UpdateService.ValidateManifest(m),"invalid manifest remains rejected");
  }
  Check(UpdateService.ManifestFileName("merged")=="update-merged.json"&&UpdateService.ManifestFileName("split")=="update.json","layout URLs keep legacy update.json split-only");
  PluginConfig.UpdateManifestMirrors.Value="https://mirror.example/update.json;http://bad.example/update.json;https://mirror.example/update.json";
  var urls=((IEnumerable<string>)Call(typeof(UpdateService),"GetManifestUrls")).ToArray();
  Check(urls.Length==3&&urls[0]=="https://mirror.example/update.json"&&urls[1].Contains("releases/latest/download/update.json")&&urls[2].Contains("/main/update.json"),"original mirror Release Raw priority and HTTPS deduplication");
  var parse=typeof(UpdateService).GetMethod("ParseVersion",BindingFlags.Static|BindingFlags.NonPublic);
  Func<string,Version> version=s=>(Version)parse.Invoke(null,new object[]{s,"QA"});
  Check(version("v1.0.25-handoff.4")==version("1.0.25.0")&&version("1.0.25.1")>version("1.0.25"),"version normalization fix retained");
  var root=Path.Combine(Path.GetTempPath(),"studentage-updater-"+Guid.NewGuid().ToString("N"));
  try {
   foreach(string location in new[]{"BepInEx/plugins","workshop/content/1991040/123/BepInEx/plugins"}){
    string dir=Path.Combine(root,location);Directory.CreateDirectory(dir);
    string target=Path.Combine(dir,"EC2BUnofficialPatch.dll"),pending=target+".pending",backup=target+".backup",audio=Path.Combine(dir,"LFBetterAudio.dll");
    File.WriteAllText(target,"old UP");File.WriteAllText(pending,"new UP");File.WriteAllText(audio,"existing BA");
    var helper=typeof(EC2BUnofficialPatch.Updater.Program);
    string log=Path.Combine(dir,"helper.log");
    var args=new[]{"0",target,pending,backup,Hash(pending),log};
    Check((int)Call(helper,"Main",new object[]{args})==0&&File.ReadAllText(target)=="new UP"&&File.ReadAllText(backup)=="old UP","upstream helper installs and backs up at "+location);
    Check(File.ReadAllText(audio)=="existing BA"&&!File.Exists(pending),"standalone BA untouched and pending consumed");
    File.WriteAllText(pending,"corrupt update");
    Check((int)Call(helper,"Main",new object[]{args})==1&&File.ReadAllText(target)=="new UP"&&File.ReadAllText(backup)=="old UP","bad pending hash rejected before replacement");
    args[4]=Hash(pending);args[3]=Path.Combine(dir,"wrong.backup");
    Check((int)Call(helper,"Main",new object[]{args})==1&&File.ReadAllText(target)=="new UP","non-adjacent backup rejected");
   }
  } finally {if(Directory.Exists(root))Directory.Delete(root,true);}
  Console.WriteLine("UPDATE_TESTS_OK "+count);return 0;
 }
}
