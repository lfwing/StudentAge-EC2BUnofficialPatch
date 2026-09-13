using System;
using System.Collections.Generic;
// Host-only dependencies; the production update service and helper are compiled unchanged.
namespace BepInEx {
 public static class Paths { public static string ConfigPath => System.IO.Path.GetTempPath(); }
 public sealed class PluginInfo { public string Location; }
}
namespace BepInEx.Bootstrap {
 public static class Chainloader { public static readonly Dictionary<string,BepInEx.PluginInfo> PluginInfos = new Dictionary<string,BepInEx.PluginInfo>(); }
}
namespace EC2BUnofficialPatch.Core {
 internal sealed class TestEntry<T> { public T Value; public TestEntry(T value){Value=value;} }
 internal static class PluginConfig {
  internal static TestEntry<bool> UpdateAutoCheck=new TestEntry<bool>(false),UpdateAutoInstall=new TestEntry<bool>(false);
  internal static TestEntry<int> UpdateCheckIntervalHours=new TestEntry<int>(6);
  internal static TestEntry<string> UpdateManifestMirrors=new TestEntry<string>("");
  internal const int DefaultUpdateCheckIntervalHours=6;
 }
 internal static class ModuleHost { internal static string GetReason(Exception e)=>e.Message; }
 internal static class PatchLog {
  internal static void Debug(string s){} internal static void Info(string s){}
  internal static void Warning(string s){} internal static void Error(string s){}
 }
}
