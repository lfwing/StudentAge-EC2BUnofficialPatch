using Mono.Cecil;
using Mono.Cecil.Cil;

string root=Path.GetFullPath(args[0]);
var directory = new DirectoryInfo(root);
var project = directory.Parent?.Parent?.FullName;
if (directory.Name != "runtime" || directory.Parent?.Name != "qa" || project == null ||
    !File.Exists(Path.Combine(project, "EC2BUnofficialPatch.csproj")) ||
    !File.Exists(Path.Combine(project, "tools", "EventCompatibilityTests", "prepare_qa.py")))
    throw new Exception("This repository's qa/runtime directory is required");
string data="Z:"+root.Replace('/','\\')+"\\data";
var report=new List<string>();
var resolver=new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.Combine(root,"StudentAge_Data/Managed"));
resolver.AddSearchDirectory(Path.Combine(root,"BepInEx/core"));
var options=new ReaderParameters {AssemblyResolver=resolver,ReadWrite=false,InMemory=true};
using var shim=AssemblyDefinition.ReadAssembly(Path.Combine(root,"StudentAge_Data/Managed/QaIsolation.dll"),options);
var pref=shim.MainModule.Types.Single(t=>t.Name=="QaPrefs");
IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types) { foreach(var type in types) { yield return type; foreach(var nested in Types(type.NestedTypes)) yield return nested; } }
var paths=Directory.GetFiles(Path.Combine(root,"StudentAge_Data/Managed"),"*.dll")
    .Concat(Directory.GetFiles(Path.Combine(root,"BepInEx/plugins"),"*.dll",SearchOption.AllDirectories)).ToArray();
foreach(var path in paths)
{
    if(Path.GetFileName(path)=="QaIsolation.dll")continue;
    using var asm=AssemblyDefinition.ReadAssembly(path,options);
    bool changed=false;
    foreach(var type in Types(asm.MainModule.Types)) foreach(var method in type.Methods.Where(m=>m.HasBody))
    {
        bool steamWrite=type.FullName.StartsWith("Steamworks.",StringComparison.Ordinal) &&
          ((type.Name=="SteamAPI" && method.Name=="RestartAppIfNecessary") || (type.Name=="SteamUserStats" && (method.Name.StartsWith("Set") || method.Name.StartsWith("ClearAchievement") || method.Name.StartsWith("StoreStats") || method.Name.StartsWith("ResetAllStats") || method.Name.StartsWith("IndicateAchievementProgress") || method.Name.StartsWith("UploadLeaderboard") || method.Name.StartsWith("UpdateAvgRateStat"))) ||
           (type.Name=="SteamRemoteStorage" && (method.Name.StartsWith("FileWrite") || method.Name.StartsWith("FileDelete") || method.Name.StartsWith("FileForget") || method.Name.StartsWith("FileShare") || method.Name.StartsWith("SetSync") || method.Name.StartsWith("SetCloud"))) ||
           (type.Name=="SteamUGC" && (method.Name.StartsWith("DownloadItem") || method.Name.StartsWith("SubscribeItem") || method.Name.StartsWith("UnsubscribeItem") || method.Name.StartsWith("CreateItem") || method.Name.StartsWith("SubmitItem") || method.Name.StartsWith("SetItem") || method.Name.StartsWith("DeleteItem") || method.Name.StartsWith("SetUserItemVote"))) ||
           (type.Name=="SteamFriends" && (method.Name=="SetRichPresence" || method.Name=="ClearRichPresence")));
        if(steamWrite)
        {
            method.Body=new MethodBody(method);var il=method.Body.GetILProcessor();
            if(method.ReturnType.FullName!="System.Void") {var v=new VariableDefinition(method.ReturnType);method.Body.Variables.Add(v);method.Body.InitLocals=true;il.Emit(OpCodes.Ldloca,v);il.Emit(OpCodes.Initobj,method.ReturnType);il.Emit(OpCodes.Ldloc,v);}
            il.Emit(OpCodes.Ret);changed=true;report.Add("BLOCK "+method.FullName);continue;
        }
        foreach(var instruction in method.Body.Instructions)
        {
            if(instruction.Operand is not MethodReference called)continue;
            if(called.DeclaringType.FullName=="UnityEngine.Application" && called.Name=="get_persistentDataPath")
            {instruction.OpCode=OpCodes.Ldstr;instruction.Operand=data;changed=true;report.Add("PATH "+method.FullName);}
            else if(called.DeclaringType.FullName=="UnityEngine.PlayerPrefs")
            {
                var match=pref.Methods.SingleOrDefault(m=>m.Name==called.Name && m.ReturnType.FullName==called.ReturnType.FullName && m.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(called.Parameters.Select(p=>p.ParameterType.FullName)));
                if(match==null)throw new Exception("Unknown PlayerPrefs call "+called.FullName);
                instruction.Operand=asm.MainModule.ImportReference(match);changed=true;report.Add("PREF "+method.FullName);
            }
        }
    }
    if(changed) {string temp=path+".isolating";asm.Write(temp);File.Move(temp,path,true);}
}
File.WriteAllLines(Path.Combine(root,"isolation-report.txt"),report);
Console.WriteLine("QA_ISOLATION_REWRITTEN "+report.Count);
if(!report.Any(x=>x.StartsWith("BLOCK")&&x.Contains("StoreStats")) || !report.Any(x=>x.StartsWith("PATH")&&x.Contains("PathDefine"))) throw new Exception("Required guards missing");
int verified=0;
foreach(var path in paths)
{
    using var asm=AssemblyDefinition.ReadAssembly(path,options);
    foreach(var type in Types(asm.MainModule.Types))foreach(var method in type.Methods.Where(m=>m.HasBody))
    {
        foreach(var instruction in method.Body.Instructions)
        {
            if(instruction.Operand is MethodReference call &&
              (call.DeclaringType.FullName=="UnityEngine.PlayerPrefs" ||
              (call.DeclaringType.FullName=="UnityEngine.Application" && call.Name=="get_persistentDataPath")))
                throw new Exception("Residual unisolated call "+method.FullName);
        }
        if(report.Contains("BLOCK "+method.FullName))
        {
            if(method.Body.Instructions.Any(i=>i.OpCode.FlowControl==FlowControl.Call))throw new Exception("Steam block retained call "+method.FullName);
            verified++;
        }
    }
}
if(verified!=report.Count(x=>x.StartsWith("BLOCK ")))throw new Exception("Steam verification count mismatch");
File.WriteAllText(Path.Combine(root,"isolation-verified.txt"),"All managed assemblies rescanned; no persistentDataPath/PlayerPrefs calls remain; Steam blocked methods verified="+verified);
