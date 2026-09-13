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
 static UpdateManifest Manifest(string layout)=>new UpdateManifest {schema=1,layout=layout,version="1.0.22",channel="stable",assetName="EC2BUnofficialPatch.dll",size=1024,sha256=new string('a',64),downloadUrls=new List<string>{"https://example.org/UP.dll"}};
 static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
 static int Main(){
  UpdateService.ValidateManifest(Manifest("merged"));Check(true,"UP accepts merged schema 1");
  foreach(string layout in new[]{"split",null,"", "unknown"})
   Reject(()=>UpdateService.ValidateManifest(Manifest(layout)),"reject old or incompatible layout: "+layout);
  var unsupported=Manifest("merged");unsupported.schema=2;
  Reject(()=>UpdateService.ValidateManifest(unsupported),"reject removed schema 2");
  foreach(var change in new Action<UpdateManifest>[] {m=>m.size=0,m=>m.size=32L*1024*1024+1,m=>m.sha256="bad",m=>m.assetName="LFBetterAudio.dll",m=>m.downloadUrls.Clear(),m=>m.downloadUrls.Add("http://example.org/UP.dll"),m=>m.version="bad",m=>m.channel="beta"}){
   var m=Manifest("merged");change(m);Reject(()=>UpdateService.ValidateManifest(m),"invalid manifest remains rejected");
  }
  Check(UpdateService.ManifestFileName=="update-merged.json","only integrated UP update feed");
  PluginConfig.UpdateManifestMirrors.Value="https://mirror.example/update.json;http://bad.example/update.json;https://mirror.example/update.json";
  var urls=((IEnumerable<string>)Call(typeof(UpdateService),"GetManifestUrls")).ToArray();
  Check(urls.Length==3&&urls[0]=="https://mirror.example/update.json"&&urls[1].Contains("releases/latest/download/update-merged.json")&&urls[2].Contains("/main/update-merged.json"),"original mirror Release Raw priority and HTTPS deduplication");
  var parse=typeof(UpdateService).GetMethod("ParseVersion",BindingFlags.Static|BindingFlags.NonPublic);
  Func<string,Version> version=s=>(Version)parse.Invoke(null,new object[]{s,"QA"});
  Check(version("v1.0.21")==version("1.0.21.0")&&version("1.0.21.1")>version("1.0.21"),"version normalization fix retained");
  Check(version("1.0.25")>version(PluginMetadata.Version),"earlier test builds require manual version rollback");
  var root=Path.Combine(Path.GetTempPath(),"studentage-updater-"+Guid.NewGuid().ToString("N"));
  try {
   foreach(string location in new[]{"BepInEx/plugins","workshop/content/1991040/123/BepInEx/plugins"}){
    string dir=Path.Combine(root,location);Directory.CreateDirectory(dir);
    string target=Path.Combine(dir,"EC2BUnofficialPatch.dll"),pending=target+".pending",backup=target+".backup",audio=Path.Combine(dir,"UnrelatedPlugin.dll");
    File.WriteAllText(target,"old UP");File.WriteAllText(pending,"new UP");File.WriteAllText(audio,"unrelated plugin");
    var helper=typeof(EC2BUnofficialPatch.Updater.Program);
    string log=Path.Combine(dir,"helper.log");
    var args=new[]{"0",target,pending,backup,Hash(pending),log};
    Check((int)Call(helper,"Main",new object[]{args})==0&&File.ReadAllText(target)=="new UP"&&File.ReadAllText(backup)=="old UP","upstream helper installs and backs up at "+location);
    Check(File.ReadAllText(audio)=="unrelated plugin"&&!File.Exists(pending),"unrelated plugin untouched and pending consumed");
    File.WriteAllText(pending,"corrupt update");
    Check((int)Call(helper,"Main",new object[]{args})==1&&File.ReadAllText(target)=="new UP"&&File.ReadAllText(backup)=="old UP","bad pending hash rejected before replacement");
    args[4]=Hash(pending);args[3]=Path.Combine(dir,"wrong.backup");
    Check((int)Call(helper,"Main",new object[]{args})==1&&File.ReadAllText(target)=="new UP","non-adjacent backup rejected");
   }
  } finally {if(Directory.Exists(root))Directory.Delete(root,true);}
  Console.WriteLine("UPDATE_TESTS_OK "+count);return 0;
 }
}
