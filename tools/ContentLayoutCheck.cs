// Runs the built UP's actual discovery/registration methods without starting Unity.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

internal static class ContentLayoutCheck
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static Assembly up;
    private static int checks;
    private static Type T(string name) => up.GetType("EC2BUnofficialPatch." + name, true);
    private static object Call(Type type, string name, params object[] args)
        => type.GetMethods(All).Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(null, args);
    private static object Get(object value, string property)
    {
        PropertyInfo info = value.GetType().GetProperty(property, All);
        return info != null ? info.GetValue(value) : value.GetType().GetField(property, All).GetValue(value);
    }
    private static string[] Strings(object value) => ((IEnumerable)value).Cast<string>().ToArray();
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
    private static object Construct(Type type, params object[] args) => Activator.CreateInstance(type, All, null, args, null);
    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, text);
    }

    public static int Main(string[] args)
    {
        try
        {
            string[] refs = { Path.GetDirectoryName(args[0]), Path.Combine(args[1], "StudentAge_Data", "Managed"), args[2] };
            AppDomain.CurrentDomain.AssemblyResolve += (sender, request) =>
            {
                string name = new AssemblyName(request.Name).Name + ".dll";
                string path = refs.Select(root => Path.Combine(root, name)).FirstOrDefault(File.Exists);
                return path == null ? null : Assembly.LoadFrom(path);
            };
            // Route BepInEx cache/template paths into this disposable workspace fixture.
            string fixture = Path.GetFullPath(Path.Combine(args[3], "content-layout-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(fixture);
            var bepinex = Assembly.LoadFrom(Path.Combine(args[2], "BepInEx.dll"));
            Type paths = bepinex.GetType("BepInEx.Paths", true);
            MethodInfo setPaths = paths.GetMethod("SetExecutablePath", All);
            object[] pathArgs = setPaths.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : null).ToArray();
            pathArgs[0] = Path.Combine(fixture, "steamapps", "common", "StudentAge", "StudentAge.exe");
            setPaths.Invoke(null, pathArgs);
            up = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            Type discovery = T("Workshop.FeatureContentDiscovery");
            string[] names = (string[])discovery.GetField("FeatureNames", All).GetValue(null);
            string mod = Path.Combine(fixture, "steamapps", "workshop", "content", "1991040", "123456");
            string sibling = Path.Combine(Path.GetDirectoryName(mod), "654321");
            Write(Path.Combine(sibling, "OnlyOtherMod", "ScreenVideo", "CustomVideo.json"), "{}");
            string[] wrappers = { "EC2BUnofficialPatch", "", Path.Combine("作者自选目录", "第二层") };
            string[] filenames = { "BetterAudio.json", null, "custombadmintoncfg.json", "ExternalLive2D.json", "LoveDrawCfg.json",
                "CustomMinigamecfg.json", "CustomScreenLyrcis.json", "Custompaper.json", "CustomVideo.json", "Worldmapscfg.json" };
            for (int i = 0; i < names.Length; i++)
            {
                string dir = Path.Combine(mod, wrappers[0], names[i]);
                Directory.CreateDirectory(dir);
                if (filenames[i] != null) Write(Path.Combine(dir, filenames[i]), "{}");
            }
            Write(Path.Combine(mod, wrappers[0], "RoleAvailabilityCfg.json"), "{\"roles\":[{\"personId\":9001,\"takeExam\":false,\"cond\":[]}]}");
            Write(Path.Combine(mod, wrappers[0], "LoveDraw", "paint", "同名.png"), "fixture");
            Write(Path.Combine(mod, wrappers[0], "Minigame", "MinigameActionCfg.json"), "{}");
            Write(Path.Combine(mod, wrappers[0], "ScreenPaper", "Custompaper.json"), "{\"papers\":[{\"id\":1,\"image\":\"Images/同名.png\"}]}");
            Write(Path.Combine(mod, wrappers[0], "ScreenPaper", "Images", "同名.png"), "fixture");
            Write(Path.Combine(mod, wrappers[0], "ScreenVideo", "CustomVideo.json"), "{\"videos\":[{\"id\":1,\"video\":\"Videos/test.mp4\"}]}");
            Write(Path.Combine(mod, wrappers[0], "ExternalLive2D", "ExternalLive2D.json"), "{\"models\":[{\"personId\":9001,\"clothId\":0,\"model\":\"Model/test.model3.json\"}]}");
            Write(Path.Combine(mod, wrappers[0], "MapExtend", "MapLocationscfg.json"), "{\"schema\":2,\"locations\":[]}");
            Write(Path.Combine(mod, wrappers[0], "MapExtend", "Cfg", "MapCfg.json"), "{}");
            Type rootType = T("Workshop.ContentRoot");
            object root = Construct(rootType, "workshop-123456", mod);
            Array roots = Array.CreateInstance(rootType, 1);
            roots.SetValue(root, 0);
            object catalog = Construct(T("Workshop.ContentRootCatalog"), roots);
            object resolver = Construct(T("Workshop.ExternalResourceResolver"), catalog);

            Type configFileType = bepinex.GetType("BepInEx.Configuration.ConfigFile", true);
            object configFile = Construct(configFileType, Path.Combine(fixture, "test.cfg"), false, null);
            configFileType.GetProperty("SaveOnConfigSet").SetValue(configFile, false);
            MethodInfo bind = configFileType.GetMethods().Single(m => m.Name == "Bind" && m.IsGenericMethodDefinition &&
                m.GetParameters().Length == 4 && m.GetParameters()[0].ParameterType == typeof(string) &&
                m.GetParameters()[3].ParameterType == typeof(string)).MakeGenericMethod(typeof(bool));
            foreach (string property in new[] { "LoveDrawExternalResources", "MinigameMechanics" })
                T("Core.PluginConfig").GetProperty(property, All).SetValue(null, bind.Invoke(configFile, new object[] { "test", property, true, "fixture" }));
            Type overlayType = T("Features.Mechanics.ModCfgOverridePatches");
            object allowedRoots = overlayType.GetField("WorkshopRoots", All).GetValue(null);
            allowedRoots.GetType().GetMethod("Add").Invoke(allowedRoots, new object[] { mod });
            overlayType.GetField("_overlayRoot", All).SetValue(null, Path.Combine(fixture, "cache"));
            string originalLove = Path.Combine(mod, "Cfgs", "zh-cn", "LoveDrawCfg.json");
            Write(originalLove, "{\"originalFileMustRemainUnchanged\":true}");
            string externalJson = null;
            if (args.Length > 4)
            {
                externalJson = File.ReadAllText(Path.Combine(args[4], "EC2BUnofficialPatch", "Minigame", "CustomMinigamecfg.json"));
                string fixtureDll = Path.Combine(mod, "plugins", "CampusMinigames.dll");
                Directory.CreateDirectory(Path.GetDirectoryName(fixtureDll));
                File.Copy(Path.Combine(args[4], "plugins", "CampusMinigames.dll"), fixtureDll);
            }

            for (int layout = 0; layout < wrappers.Length; layout++)
            {
                string container = Path.Combine(mod, wrappers[layout]);
                if (layout > 0)
                {
                    Directory.CreateDirectory(container);
                    foreach (string name in names)
                    {
                        string from = Path.GetFullPath(Path.Combine(mod, wrappers[layout - 1], name));
                        string to = Path.GetFullPath(Path.Combine(container, name));
                        Check(from.StartsWith(fixture + Path.DirectorySeparatorChar) && to.StartsWith(fixture + Path.DirectorySeparatorChar), "fixture move containment");
                        Directory.Move(from, to);
                    }
                    File.Move(Path.Combine(mod, wrappers[layout - 1], "RoleAvailabilityCfg.json"), Path.Combine(container, "RoleAvailabilityCfg.json"));
                }
                foreach (string name in names)
                    Check(Strings(Call(discovery, "Directories", mod, name, null)).SequenceEqual(new[] { Path.Combine(container, name) }), "directory discovery: " + layout + "/" + name);
                Check(Strings(Call(discovery, "Directories", mod, "OnlyOtherMod", null)).Length == 0, "must stay inside owning Mod");
                Check(Strings(Call(discovery, "FilesNamed", mod, "RoleAvailabilityCfg.json")).Single() == Path.Combine(container, "RoleAvailabilityCfg.json"), "standalone role config");
                foreach (string feature in new[] { "ScreenPaper", "ScreenVideo" })
                {
                    object registry = Call(T("Features.ScreenEffects." + feature + "Registry"), "Load", roots);
                    Check((int)Get(registry, "Count") == 1, feature + " actual registration");
                    object[] lookup = { 1, null };
                    Check((bool)registry.GetType().GetMethod("TryGet", All).Invoke(registry, lookup), feature + " lookup");
                    string property = feature == "ScreenPaper" ? "ImagePath" : "VideoPath";
                    Check(((string)Get(lookup[1], property)).StartsWith(Path.Combine(container, feature) + Path.DirectorySeparatorChar), feature + " relocated resource base");
                }
                Check(Strings(Call(T("Features.Mechanics.CustomBadmintonRegistry"), "EnumerateConfigFiles", roots)).Single() == Path.Combine(container, "custombadminton", "custombadmintoncfg.json"), "badminton config reader");
                Check(Strings(Call(T("Features.Mechanics.Minigames.CustomMinigameRegistry"), "DiscoverFiles", catalog)).Single() == Path.Combine(container, "Minigame", "CustomMinigamecfg.json"), "minigame config reader");
                Check((int)Get(Call(T("Services.RoleAvailabilityService"), "Load", catalog), "Count") == 1, "role actual registration");
                object live = Call(T("Features.Mechanics.ExternalLive2DRegistry"), "Load", catalog);
                Check((int)Get(live, "Count") == 1, "Live2D actual registration");
                object[] liveLookup = { 9001, 0, 0, null };
                Check((bool)live.GetType().GetMethod("TryGet", All).Invoke(live, liveLookup), "Live2D lookup");
                Check((string)Get(liveLookup[3], "RootId") == "workshop-123456", "Live2D owner independent of wrapper");
                Check((string)Get(liveLookup[3], "ModelPath") == Path.Combine(container, "ExternalLive2D", "Model", "test.model3.json"), "Live2D model relative path");
                object resources = Call(T("Workshop.ResourceIndex"), "Build", catalog, resolver);
                Check((int)Get(resources, "LoveDrawImageCount") == 1, "LoveDraw actual image registration");
                Check(Strings(Get(resources, "LyricFiles")).Single() == Path.Combine(container, "ScreenLyrcis", "CustomScreenLyrcis.json"), "lyrics actual file registration");
                foreach (string feature in new[] { "LoveDraw", "Minigame" })
                {
                    string cfg = feature == "LoveDraw" ? "LoveDrawCfg.json" : "MinigameActionCfg.json";
                    object decision = Call(T("Features.Mechanics.ModCfgOverridePatches"), "TryCreateDecision", Path.Combine(mod, "Cfgs", "zh-cn"), mod, true, feature, feature, cfg);
                    Check((string)Get(decision, "SourcePath") == Path.Combine(container, feature, cfg), "enhanced CFG reader: " + feature);
                }
                object[] overlayArgs = { (ulong)123456, Path.GetDirectoryName(originalLove), null, null };
                Check((bool)overlayType.GetMethod("TryBuildOverlay", All).Invoke(null, overlayArgs), "actual enhanced CFG overlay");
                Check(File.ReadAllText(Path.Combine((string)overlayArgs[2], "LoveDrawCfg.json")) == "{}", "enhanced LoveDraw overrides runtime copy");
                Check(File.Exists(Path.Combine((string)overlayArgs[2], "MinigameActionCfg.json")), "enhanced Minigame injects absent original CFG");
                Check(File.ReadAllText(originalLove).Contains("originalFileMustRemainUnchanged"), "Workshop original remains unchanged");
                Type minigames = T("Features.Mechanics.Minigames.CustomMinigameRegistry");
                string minigameJson = Path.Combine(container, "Minigame", "CustomMinigamecfg.json");
                Check((string)Call(minigames, "ResolveRelativePath", minigameJson, "Games/test.dll", mod) == Path.Combine(container, "Minigame", "Games", "test.dll"), "external minigame DLL relocated base");
                string siblingDll = layout == 0 ? "../../plugins/CampusMinigames.dll" :
                    layout == 1 ? "../plugins/CampusMinigames.dll" : "../../../plugins/CampusMinigames.dll";
                Check((string)Call(minigames, "ResolveRelativePath", minigameJson, siblingDll, mod) == Path.Combine(mod, "plugins", "CampusMinigames.dll"), "external DLL may use sibling folder within owning Mod");
                foreach (string invalid in new[] { "../../../../outside.dll", Path.Combine(mod, "plugins", "CampusMinigames.dll"), "C:outside.dll", "//server/outside.dll" })
                    Check(Call(minigames, "ResolveRelativePath", minigameJson, invalid, mod) == null, "reject DLL outside Mod or absolute path: " + invalid);
                if (externalJson != null)
                {
                    Write(minigameJson, externalJson.Replace("../../plugins/CampusMinigames.dll", siblingDll));
                    object games = Call(minigames, "Load", catalog);
                    Check((int)Get(games, "ExplicitCount") == 13, "all 13 real external DLL entry classes register in layout " + layout);
                    var definitions = (IDictionary)minigames.GetField("_definitions", All).GetValue(games);
                    for (int id = 9101; id <= 9113; id++)
                    {
                        Check(definitions.Contains(id), "real external ID " + id);
                        object definition = definitions[id];
                        Check((string)definition.GetType().GetField("DllPath", All).GetValue(definition) == Path.Combine(mod, "plugins", "CampusMinigames.dll"), "real external DLL path " + id);
                        Check(definition.GetType().GetField("ExternalType", All).GetValue(definition) != null, "real public API type validation " + id);
                    }
                }
                Type worlds = T("Features.Mechanics.MapExtend.WorldMapsContent");
                object source = ((IEnumerable)Call(worlds, "Sources", roots)).Cast<object>().Single(s => (string)Get(s, "Id") == "workshop-123456");
                Check((ulong)Call(worlds, "ModId", source) == 123456, "WorldMaps owner independent of depth");
                Check((string)Get(source, "Path") == Path.Combine(container, "MapExtend"), "WorldMaps and MapLocations shared source");
                string[] hot = Strings(Call(T("Services.UpExtensionJsonHotReload"), "EnumerateJsonFiles", catalog));
                Check(hot.Contains(Path.Combine(container, "ExternalLive2D", "ExternalLive2D.json")), "F9 Live2D source");
                Check(hot.Contains(Path.Combine(container, "MapExtend", "Worldmapscfg.json")), "F9 WorldMaps source");
                Check(!hot.Contains(Path.Combine(container, "MapExtend", "Cfg", "MapCfg.json")), "generated original CFG must stay excluded");
                Type audioDiscovery = up.GetType("LFBetterAudio.Discovery.BetterAudioPackageDiscovery", true);
                object packages = Call(audioDiscovery, "DiscoverWorkshopPackages", Path.Combine(fixture, "steamapps", "common", "StudentAge"));
                object package = ((IEnumerable)packages).Cast<object>().Single(p => (string)Get(p, "ModRootDirectory") == mod);
                Check((string)Get(package, "BetterAudioDirectory") == Path.Combine(container, "BetterAudio"), "BetterAudio actual package discovery");
                Check((string)Get(package, "WorkshopItemId") == "123456", "BetterAudio owner independent of wrapper");
                Write(Path.Combine(container, "BetterAudio", "BetterAudio.json"), "{\"audios\":[{\"id\":9001,\"audioPath\":\"Audio/test.mp3\",\"timelinePath\":\"Timeline/test.lrc\"}]}");
                Type audioStore = up.GetType("LFBetterAudio.Config.BetterAudioConfigStore", true);
                object audio = Call(audioStore, "LoadAll", packages);
                object[] audioLookup = { 9001, null };
                Check((bool)audioStore.GetMethod("TryGet").Invoke(audio, audioLookup), "BetterAudio actual config registration");
                MethodInfo resolveAudio = audioStore.GetMethods().Single(m => m.Name == "ResolvePath" && m.GetParameters()[0].ParameterType != typeof(string));
                Check((string)resolveAudio.Invoke(null, new[] { audioLookup[1], "Audio/test.mp3" }) == Path.Combine(container, "BetterAudio", "Audio", "test.mp3"), "BetterAudio relocated audio base");
            }
            string last = Path.Combine(mod, wrappers[2]);
            foreach (string relative in new[] { "../other.png", "C:\\outside.png", "/outside.png", "//server/file", "C:outside.png" })
                Check(Call(discovery, "RelativeFile", last, relative) == null, "reject non-relative path: " + relative);
            Check((string)Call(discovery, "RelativeFile", last, "Images\\中文.png") == Path.Combine(last, "Images", "中文.png"), "relative path separators and Unicode");
            Write(Path.Combine(mod, "ScreenVideo", "CustomVideo.json"), "{}");
            Write(Path.Combine(mod, "EC2BUnofficialPatch", "ScreenVideo", "CustomVideo.json"), "{}");
            string[] ordered = Strings(Call(discovery, "Directories", mod + Path.DirectorySeparatorChar, "screenvideo", null));
            Check(ordered.SequenceEqual(new[] { Path.Combine(mod, "EC2BUnofficialPatch", "ScreenVideo"), Path.Combine(mod, "ScreenVideo"), Path.Combine(last, "ScreenVideo") }), "2 > 1 > 3 stable order and case insensitive discovery");
            Directory.CreateDirectory(Path.Combine(last, "ScreenVideo", "ScreenVideo"));
            Check(Strings(Call(discovery, "Directories", mod, "ScreenVideo", null)).Length == 3, "nested same-name resource folder must not double-register");
            Type hotReload = T("Services.UpExtensionJsonHotReload");
            Call(hotReload, "CaptureBaseline", catalog);
            string changed = Path.Combine(last, "ExternalLive2D", "ExternalLive2D.json");
            File.AppendAllText(changed, " ");
            Check(Strings(Get(Call(hotReload, "Prepare", catalog), "ChangedPaths")).Contains(changed), "F9 detects edits in arbitrary wrapper");
            Write(changed, "{malformed");
            bool rejected = false;
            try { Call(hotReload, "Prepare", catalog); }
            catch (TargetInvocationException error) { rejected = error.InnerException is InvalidDataException; }
            Check(rejected, "F9 rejects malformed relocated JSON before switching runtime");
            if (args.Length > 4)
            {
                Type minigames = T("Features.Mechanics.Minigames.CustomMinigameRegistry");
                string actualMod = Path.GetFullPath(args[4]);
                Array actualRoots = Array.CreateInstance(rootType, 1);
                actualRoots.SetValue(Construct(rootType, "workshop-" + Path.GetFileName(actualMod), actualMod), 0);
                object actualRegistry = Call(minigames, "Load", Construct(T("Workshop.ContentRootCatalog"), actualRoots));
                Check((int)Get(actualRegistry, "ExplicitCount") == 13, "unmodified installed Mod registers all 13 entries");
                Console.WriteLine("PASS: installed external Mod's unchanged JSON and DLL register IDs 9101-9113; no game entry or Open() invoked.");
            }
            MapExtendChecks(fixture, bepinex, refs);
            Console.WriteLine("PASS: " + checks + " layout/registration/relative-path/MapExtend checks. Fixtures: " + fixture);
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }

    private static void MapExtendChecks(string fixture, Assembly bepinex, string[] refs)
    {
        Type cfg = T("Core.PluginConfig"), fileType = bepinex.GetType("BepInEx.Configuration.ConfigFile", true);
        foreach (bool master in new[] { false, true }) foreach (bool nested in new[] { false, true })
        {
            string path = Path.Combine(fixture, "migration-" + master + nested + ".cfg");
            Write(path, "[机制]\n大地图扩展 = " + master + "\n[优化]\n多mod地图子地点兼容 = " + !master + "\n地图子地点扩展 = " + nested + "\n");
            object config = Construct(fileType, path, false, null);
            Call(cfg, "Initialize", config);
            Check((bool)Get(cfg.GetProperty("MapExtend", All).GetValue(null), "Value") == master, "old master migration");
            Check((bool)Get(cfg.GetProperty("MapFloorHierarchyExtension", All).GetValue(null), "Value") == nested, "nested migration keeps explicit value");
            string text = File.ReadAllText(path);
            Check(!text.Contains("多mod地图子地点兼容 =") && !text.Contains("大地图扩展 ="), "retired config keys removed");
            int a = text.IndexOf("地图扩展与优化 ="), b = text.IndexOf("地图子地点扩展 =", a);
            Check(a >= 0 && b > a && !text.Substring(a, b-a).Split('\n').Skip(1).Any(l => l.Contains(" = ")), "config options adjacent");
        }
        string fresh = Path.Combine(fixture, "fresh-map.cfg");
        Call(cfg, "Initialize", Construct(fileType, fresh, false, null));
        Check((bool)Get(cfg.GetProperty("MapExtend", All).GetValue(null), "Value"), "new master enabled by default");
        Check(!(bool)Get(cfg.GetProperty("MapFloorHierarchyExtension", All).GetValue(null), "Value"), "nested disabled by default");
        string priority = Path.Combine(fixture, "new-priority.cfg");
        Write(priority, "[机制]\n地图扩展与优化 = false\n大地图扩展 = true\n地图子地点扩展 = false\n[优化]\n地图子地点扩展 = true\n");
        Call(cfg, "Initialize", Construct(fileType, priority, false, null));
        Check(!(bool)Get(cfg.GetProperty("MapExtend", All).GetValue(null), "Value") && !(bool)Get(cfg.GetProperty("MapFloorHierarchyExtension", All).GetValue(null), "Value"), "new config takes priority");
        var json = Assembly.LoadFrom(refs.Select(r => Path.Combine(r, "Newtonsoft.Json.dll")).First(File.Exists));
        Type jobject = json.GetType("Newtonsoft.Json.Linq.JObject");
        Func<string, object> parse = s => jobject.GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { s });
        Type registry = T("Features.Mechanics.MapExtend.SceneObjectRegistry");
        // Exercise the selection used by PreparePlayer without invoking Unity.
        Type runtime = T("Features.Mechanics.MapExtend.WorldMapsRuntime");
        NewGameFunctionChecks(runtime, refs);
        IntroductionHookChecks(refs);
        object world = Construct(T("Features.Mechanics.MapExtend.WorldMapDefinition"));
        world.GetType().GetField("Id", All).SetValue(world, 72);
        world.GetType().GetField("TravelMaps", All).SetValue(world, new[] { 31 });
        world.GetType().GetField("AutoUnlockMaps", All).SetValue(world, new[] { 32, 32001, 32 });
        Func<int[]> automatic = () => ((IEnumerable)Call(runtime, "AutomaticUnlockMaps", world)).Cast<int>().ToArray();
        Check(Enumerable.SequenceEqual(automatic(), new[] { 32, 32001, 31 }), "transport is compulsory and explicit child selection is preserved");
        world.GetType().GetField("AutoUnlockMaps", All).SetValue(world, new[] { 31 });
        Check(Enumerable.SequenceEqual(automatic(), new[] { 31 }), "station selection does not implicitly open its floors");
        world.GetType().GetField("AutoUnlockMaps", All).SetValue(world, new int[0]);
        Check(Enumerable.SequenceEqual(automatic(), new[] { 31 }), "transport cannot opt out of automatic opening");
        world.GetType().GetField("Id", All).SetValue(world, 0);
        world.GetType().GetField("TravelMaps", All).SetValue(world, new[] { 12 });
        world.GetType().GetField("AutoUnlockMaps", All).SetValue(world, new[] { 12, 13 });
        Check(Enumerable.SequenceEqual(automatic(), new[] { 13 }), "native station still excluded from automatic opening");
        string good = "{\"key\":\"door\",\"mapId\":2,\"personId\":990101,\"hitArea\":{\"type\":\"polygon\",\"points\":[[0,0],[10,0],[10,10],[0,10]]}}";
        object entry = Call(registry, "Parse", parse(good), Path.Combine(fixture, "SceneObjectscfg.json"));
        string modern = good.Substring(0, good.Length-1) + ",\"name\":\"2-门\",\"bgId\":201,\"normalCondition\":[[1,1,1,10]],\"clickCondition\":[],\"click\":{\"type\":\"scene\",\"target\":1}}";
        object newEntry = Call(registry, "Parse", parse(modern), Path.Combine(fixture, "SceneObjectscfg.json"));
        Check((bool)Get(newEntry, "Modern") && (int)Get(newEntry, "Target") == 1, "new scene action supports shared home move");
        Check((bool)Call(registry, "MatchesBackgroundId", newEntry, 201) && !(bool)Call(registry, "MatchesBackgroundId", newEntry, 202), "new objects bind to actual BgId");
        Check(((IList)Get(newEntry, "NormalCondition")).Count == 1 && ((IList)Get(newEntry, "ClickCondition")).Count == 0, "normal and click conditions remain independent");
        ObjectEffectChecks(newEntry, registry, parse, fixture, refs);
        foreach (string invalid in new[] { modern.Replace("\"bgId\":201", "\"bgId\":0"), modern.Replace("[[1,1,1,10]]", "[1,1,1,10]"), modern.Replace("\"target\":1", "\"target\":0"), modern.Replace("\"type\":\"scene\"", "\"type\":\"function\"") })
        {
            bool bad = false; try { Call(registry, "Parse", parse(invalid), Path.Combine(fixture, "SceneObjectscfg.json")); } catch (TargetInvocationException ex) { bad = ex.InnerException is InvalidDataException; }
            Check(bad, "reject invalid schema2 object: " + invalid);
        }
        Check((bool)Call(registry, "MatchesBackground", entry, false, "bg/example", false), "main background match");
        Check(!(bool)Call(registry, "MatchesBackground", entry, true, "bg/example", false), "second background excluded");
        object home = Call(registry, "Parse", parse(good.Replace("\"mapId\":2", "\"mapId\":1")), Path.Combine(fixture, "SceneObjectscfg.json"));
        Check(home != null, "MainView object accepted");
        foreach (string invalid in new[] { good.Replace("990101", "0"), good.Replace("\"key\":\"door\"", "\"key\":\"\""), good.Replace("\"personId\":990101", "\"personId\":990101,\"click\":{\"type\":\"changeScene\",\"target\":1}"), good.Replace("\"personId\":990101", "\"personId\":990101,\"visual\":{\"normal\":{\"image\":\"../escape.png\",\"position\":[0,0],\"size\":[10,10]}}") })
        {
            bool bad = false; try { Call(registry, "Parse", parse(invalid), Path.Combine(fixture, "SceneObjectscfg.json")); }
            catch (TargetInvocationException e) { bad = e.InnerException is InvalidDataException; }
            Check(bad, "invalid scene object rejected");
        }
        Type rootType = T("Workshop.ContentRoot");
        string mod = Path.Combine(fixture, "scene-mod");
        Array roots = Array.CreateInstance(rootType, 1); roots.SetValue(Construct(rootType, "workshop-123456", mod), 0);
        foreach (string wrapper in new[] { "EC2BUnofficialPatch", "", "作者目录" })
        {
            string directory = Path.Combine(mod, wrapper, "MapExtend");
            Write(Path.Combine(directory, "SceneObjectscfg.json"), "{\"schema\":2,\"objects\":[" + modern + "]}");
            object loaded = Call(registry, "Load", roots);
            Check(((IList)loaded.GetType().GetField("Entries", All).GetValue(loaded)).Count == 1, "scene objects in three layouts");
            Directory.Delete(directory, true);
        }
        string legacy = Path.Combine(mod, "WorldMaps");
        Write(Path.Combine(legacy, "SceneObjectscfg.json"), "{\"schema\":1,\"objects\":[" + good + "]}");
        Check(((IList)Call(registry, "Load", roots).GetType().GetField("Entries", All).GetValue(Call(registry, "Load", roots))).Count == 1, "legacy folder fallback");
        Write(Path.Combine(mod, "MapExtend", "SceneObjectscfg.json"), "{\"schema\":1,\"objects\":[]}");
        Check(((IList)Call(registry, "Load", roots).GetType().GetField("Entries", All).GetValue(Call(registry, "Load", roots))).Count == 0, "new folder takes priority without duplicate legacy registration");
    }

    private static void UnsafeFunctionRegistration()
    {
        // Reproduce the reported pre-fix write, even when the row already exists.
        Type cfg = up.GetType("EC2BUnofficialPatch.Features.Mechanics.MapExtend.WorldMapsRuntime").GetField("TravelFunction", All).FieldType;
        var game = cfg.Assembly;
        ((IDictionary)game.GetType("Config.Cfg").GetProperty("FuncCfgMap", All).GetValue(null))[160] =
            T("Features.Mechanics.MapExtend.WorldMapsRuntime").GetField("TravelFunction", All).GetValue(null);
    }

    private static void NewGameFunctionChecks(Type runtime, string[] refs)
    {
        var game = Assembly.LoadFrom(Path.Combine(refs[1], "Assembly-CSharp.dll"));
        Type cfg = game.GetType("Config.Cfg", true), rowType = game.GetType("Config.FuncCfg", true);
        Type mgrType = game.GetType("FuncMgr", true), modeType = game.GetType("GameMode", true);
        PropertyInfo tableField = cfg.GetProperty("FuncCfgMap", All);
        object saved = tableField.GetValue(null);
        Type harmony = T("Features.Mechanics.MapExtend.MapExtendEffects").GetMethod("InstallMove", All).GetParameters()[0].ParameterType;
        Type harmonyMethod = harmony.Assembly.GetType("HarmonyLib.HarmonyMethod");
        object owner = Construct(harmony, "qa.newgame.functions");
        MethodInfo patch = harmony.GetMethods().Where(m => m.Name == "Patch" && !m.IsStatic).OrderBy(m => m.GetParameters().Length).First();
        try
        {
            Func<IDictionary> table = () => {
                var rows = (IDictionary)Construct(tableField.PropertyType);
                foreach (int id in new[] { 99021, 99022 })
                {
                    object row = Construct(rowType);
                    rowType.GetField("id").SetValue(row, id);
                    rowType.GetField("debug").SetValue(row, id == 99021 ? 2 : 1);
                    rowType.GetField("parm").SetValue(row, new List<int>());
                    rowType.GetField("chain").SetValue(row, new List<int>());
                    rows[id] = row;
                }
                tableField.SetValue(null, rows); return rows;
            };
            object manager = game.GetType("Sdk.Singleton`1").MakeGenericType(mgrType).GetProperty("Ins").GetValue(null);
            mgrType.GetMethod("Init").Invoke(manager, null);
            IDictionary oldRows = table(); Call(runtime, "EnsureFunction");
            object[] patchArgs = new object[patch.GetParameters().Length];
            patchArgs[0] = game.GetType("FuncData").GetMethod("OpenFunc");
            patchArgs[1] = Construct(harmonyMethod, typeof(ContentLayoutCheck).GetMethod(nameof(UnsafeFunctionRegistration), All));
            patch.Invoke(owner, patchArgs);
            bool reproduced = false;
            try { mgrType.GetMethod("SetIsGuideMode").Invoke(manager, new[] { Enum.Parse(modeType, "GuideOff") }); }
            catch (TargetInvocationException ex) { reproduced = ex.InnerException is InvalidOperationException && ex.InnerException.StackTrace.Contains("SetIsGuideMode"); }
            Check(reproduced, "pre-fix unconditional write reproduces the exact native SetIsGuideMode exception");
            harmony.GetMethod("UnpatchSelf").Invoke(owner, null);
            Call(runtime, "InstallFunctionHooks", owner);
            object info = Call(harmony, "GetPatchInfo", game.GetType("FuncData").GetMethod("OpenFunc"));
            Check(!((IEnumerable)Get(info, "Prefixes")).Cast<object>().Any(p => ((MethodInfo)Get(p, "PatchMethod")).DeclaringType == runtime), "OpenFunc has no cfg registration prefix");
            foreach (string mode in new[] { "GuideOn", "GuideOff", "Dev" })
                foreach (bool existing in new[] { false, true })
                {
                    IDictionary rows = table();
                    if (existing) rows[160] = Construct(rowType); // overwritten before native enumeration
                    mgrType.GetMethod("Init").Invoke(manager, null);
                    mgrType.GetMethod("SetIsGuideMode").Invoke(manager, new[] { Enum.Parse(modeType, mode) });
                    Check(rows.Count == 3 && ReferenceEquals(rows[160], runtime.GetField("TravelFunction", All).GetValue(null)), mode + " registers the reserved function before foreach");
                    Check((bool)mgrType.GetMethod("IsFuncOpened").Invoke(manager, new object[] { 99021 }), mode + " opens always-enabled native function");
                    Check((bool)mgrType.GetMethod("IsFuncOpened").Invoke(manager, new object[] { 99022 }) == (mode != "GuideOn"), mode + " preserves guide-dependent function behavior");
                    Check((bool)mgrType.GetMethod("IsFuncOpened").Invoke(manager, new object[] { 160 }) == (mode == "Dev"), mode + " preserves travel function debug=0");
                    var enumeration = rows.GetEnumerator(); enumeration.MoveNext();
                    Call(runtime, "EnsureFunction");
                    Check(enumeration.MoveNext(), "repeated registration does not invalidate an existing iterator");
                }
        }
        finally { harmony.GetMethod("UnpatchSelf").Invoke(owner, null); tableField.SetValue(null, saved); }
    }

    private static void IntroductionHookChecks(string[] refs)
    {
        var game = Assembly.LoadFrom(Path.Combine(refs[1], "Assembly-CSharp.dll"));
        Type intro = T("Features.Mechanics.MapExtend.SceneObjectIntroduction");
        Type harmony = intro.GetMethod("Install", All).GetParameters()[0].ParameterType;
        Type processor = harmony.Assembly.GetType("HarmonyLib.PatchProcessor");
        MethodInfo read = processor.GetMethods(All).Single(m => m.Name == "GetOriginalInstructions" &&
            !m.GetParameters().Any(p => p.ParameterType.IsByRef));
        MethodInfo original = game.GetType("HintHelper").GetMethod("ShowTalk");
        Func<MethodInfo, object[]> instructions = method =>
        {
            var arguments = new object[read.GetParameters().Length]; arguments[0] = method;
            return ((IEnumerable)read.Invoke(null, arguments)).Cast<object>().ToArray();
        };
        object[] installation = instructions(intro.GetMethod("Install", All));
        Check(!installation.Select(i => Get(i, "operand")).OfType<Type>().Any(t =>
            t == game.GetType("View.Main.MainView") || t == game.GetType("View.Main.MapSceneView")),
            "introduction installer does not target native home/map object methods");
        Check(installation.Select(i => Get(i, "operand")).OfType<string>().SequenceEqual(new[] { "OnOpen", "Opened", "OnClose", "Closed" }),
            "introduction installer only targets tagged dialog open/close lifecycle");
        foreach (string name in new[] { "OnClickCal", "OnClickSofa", "OnClickFruit", "OnClickJiangbei", "ShowIntroduction" })
        {
            var method = game.GetType(name == "ShowIntroduction" ? "View.Main.MapSceneView" : "View.Main.MainView").GetMethod(name, All);
            object[] before = instructions(method);
            Check(before.Count(i => Equals(Get(i, "operand"), original)) == 1, "native object introduction has one presentation call: " + name);
        }
        Action callback = () => { };
        object[] parameters = (object[])Call(intro, "Parameters", -1, "介绍", callback, 0, -1, null);
        Check((int)parameters[5] == -1, "UP introduction bypasses native BgCfg texture loading");
        Check((int)parameters[0] == -1 && (string)parameters[1] == "介绍" && ReferenceEquals(parameters[2], callback),
            "UP introduction retains role/text/callback");
        Check(!instructions(intro.GetMethod("ShowTalk", All)).Select(i => Get(i, "operand")).OfType<MethodInfo>()
            .Any(m => m.Name == "SetTextureUrl"), "UP introduction does not request a replacement texture");
        Type visual = T("Features.Mechanics.MapExtend.SceneIntroductionVisual");
        Type vector = visual.GetMethod("Flatten", All).ReturnType;
        foreach (float depth in new[] { -250f, -100f, 50f })
        {
            object flattened = Call(visual, "Flatten", Construct(vector, 123f, -456f, depth));
            Check((float)Get(flattened, "x") == 123f && (float)Get(flattened, "y") == -456f && (float)Get(flattened, "z") == 0f,
                "dialog copies retain XY but remain on their own background plane: " + depth);
        }
        Check(intro.GetMethod("Closed", All).GetParameters()[0].ParameterType == game.GetType("Sdk.BaseView"), "inherited NewTalk close accepts every native dialog type");
        Console.WriteLine("PASS: native SetIsGuideMode regression, native object presentation unchanged, no raw BgCfg request, dialog-plane geometry. Unity UI hooks/rendering require in-game verification.");
    }

    private static void ObjectEffectChecks(object entry, Type registry, Func<string, object> parse, string fixture, string[] refs)
    {
        Type state = T("Features.Mechanics.MapExtend.SceneObjectEffectState");
        foreach (int mode in new[] { 1, -1, 2, -2 })
        {
            object[] input = { new List<float> { 100, 11, mode, 990101 }, 0, 0 };
            Check((bool)state.GetMethod("Read", All).Invoke(null, input) && (int)input[1] == mode && (int)input[2] == 990101, "read object effect " + mode);
        }
        foreach (List<float> invalid in new[] { null, new List<float> { 100, 11, 1 }, new List<float> { 101, 11, 1, 2 },
            new List<float> { 100, 1, 1, 2 }, new List<float> { 100, 11, 3, 2 }, new List<float> { 100, 11, 1, 0 },
            new List<float> { 100, 11, 1, 2.5f }, new List<float> { 100, 11, 1, float.NaN },
            new List<float> { 100, 11, 1, float.PositiveInfinity }, new List<float> { 100, 11, 1, (float)int.MaxValue } })
            Check(!(bool)state.GetMethod("Read", All).Invoke(null, new object[] { invalid, 0, 0 }), "reject malformed object effect");
        Type worldState = T("Features.Mechanics.MapExtend.WorldMapState");
        var normal = (Dictionary<int, bool>)worldState.GetField("ObjectNormal", All).GetValue(null);
        var click = (Dictionary<int, bool>)worldState.GetField("ObjectClick", All).GetValue(null);
        Call(worldState, "Clear");
        string baseRow = "{\"key\":\"effect-test\",\"name\":\"test\",\"mapId\":2,\"bgId\":201,\"personId\":990101,\"hitArea\":{\"type\":\"polygon\",\"points\":[[0,0],[10,0],[10,10]]},\"click\":{\"type\":\"introduction\"},\"visual\":{";
        Func<string, object> visual = v => Call(registry, "Parse", parse(baseRow + v + "}}"), Path.Combine(fixture, "SceneObjectscfg.json"));
        string layer = "{\"image\":\"Images/test.png\",\"position\":[0,0],\"size\":[10,10]}";
        object both = visual("\"normal\":" + layer + ",\"hover\":" + layer), hover = visual("\"hover\":" + layer);
        Func<object, bool, bool, bool> resolve = (e, condition, isClick) => (bool)Call(state, "Resolve", e, condition, isClick);
        Check(!resolve(both, false, false) && resolve(both, true, false), "unset ordinary image state follows conditions");
        Check(!resolve(both, false, true) && resolve(both, true, true), "unset click state follows conditions");
        normal[990101] = true; click[990101] = false;
        Check(resolve(both, false, false) && !resolve(both, true, true), "show and disable click independently override conditions");
        normal[990101] = false; click[990101] = true;
        Check(!resolve(both, true, false) && resolve(both, false, true), "hide and enable click independently override conditions");
        Check(!(bool)Call(state, "HasImage", hover, 1) && (bool)Call(state, "HasImage", hover, 2), "hover-only object accepts click effect but not ordinary image effect");
        Check(!(bool)Call(state, "HasImage", entry, 2) && !resolve(entry, false, true), "image-free region cannot gain meaningless override");
        Check(resolve(hover, true, false), "missing ordinary image never consumes normal override");
        object legacy = visual("\"normal\":" + layer);
        legacy.GetType().GetField("Modern", All).SetValue(legacy, false);
        Check((bool)Call(state, "HasImage", legacy, 1) && !resolve(legacy, true, false) && resolve(legacy, false, true), "legacy image-backed object shares independent effect overrides");
        // Use the actual native in-memory save API; no disk or Unity scene calls.
        Call(worldState, "Save", 72);
        Call(worldState, "Clear");
        Check(!normal.Any() && !click.Any(), "new game clears both override dictionaries");
        Check((int)Call(worldState, "Load") == 72 && normal[990101] == false && click[990101] == true, "native save/load restores both explicit states");
        var game = Assembly.LoadFrom(Path.Combine(refs[1], "Assembly-CSharp.dll"));
        object saveMgr = game.GetType("SaveMgrEx").GetField("gameSaveMgr", All).GetValue(null);
        var saved = (IDictionary)saveMgr.GetType().GetField("saveDict", All).GetValue(saveMgr);
        saved.Remove("EC2BUnofficialPatch.WorldMaps.v2.object-normal"); saved.Remove("EC2BUnofficialPatch.WorldMaps.v2.object-click");
        Call(worldState, "Load");
        Check(!normal.Any() && !click.Any() && !resolve(both, false, false), "old save without object keys returns to conditions");
        // Install actual Harmony providers with two distribution owners, in both orders.
        Type provider = T("Features.Mechanics.MapExtend.MapExtendEffects");
        Type harmony = provider.GetMethod("InstallMove", All).GetParameters()[0].ParameterType, useful = game.GetType("Effect.EffectorUseful", true);
        foreach (string[] owners in new[] { new[] { "qa.full", "qa.standalone" }, new[] { "qa.standalone", "qa.full" } })
        {
            object first = Construct(harmony, owners[0]), second = Construct(harmony, owners[1]);
            Call(provider, "InstallMove", first); Call(provider, "InstallMove", second);
            Call(provider, "Install", second); Call(provider, "Install", first);
            foreach (string method in new[] { "OnRun", "OnToString" })
            {
                object patches = Call(harmony, "GetPatchInfo", useful.GetMethod(method, All));
                object[] prefixes = ((IEnumerable)Get(patches, "Prefixes")).Cast<object>().Where(p => ((MethodInfo)Get(p, "PatchMethod")).DeclaringType == provider).ToArray();
                Check(prefixes.Length == 2, "single move and object provider with owners " + owners[0] + "/" + owners[1]);
            }
            foreach (object h in new[] { first, second }) harmony.GetMethod("UnpatchSelf").Invoke(h, null);
        }
        // A different assembly with the same prefix contract must be recognized too.
        var foreignAssembly = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("MapExtendStandaloneContractCheck"), AssemblyBuilderAccess.Run);
        var foreignModule = foreignAssembly.DefineDynamicModule("contract");
        Func<string, string, Type> foreign = (typeName, methodName) => {
            var type = foreignModule.DefineType(typeName, TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
            var method = type.DefineMethod(methodName, MethodAttributes.Public | MethodAttributes.Static, typeof(bool), Type.EmptyTypes);
            var il = method.GetILGenerator(); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret); return type.CreateType();
        };
        Type foreignEffects = foreign("Foreign.MapExtendEffects", "RunUseful");
        object foreignOwner = Construct(harmony, "qa.foreign");
        Action<MethodInfo, MethodInfo> patch = (target, prefix) => {
            var method = harmony.GetMethods().Where(m => m.Name == "Patch" && !m.IsStatic && m.GetParameters().Length >= 3 && m.GetParameters()[1].ParameterType.Name == "HarmonyMethod").OrderBy(m => m.GetParameters().Length).First();
            var parameters = method.GetParameters(); var args = new object[parameters.Length]; args[0] = target;
            args[1] = Construct(parameters[1].ParameterType, prefix); method.Invoke(foreignOwner, args);
        };
        patch(useful.GetMethod("OnRun", All), foreignEffects.GetMethod("RunUseful"));
        object own = Construct(harmony, "qa.own"); Call(provider, "InstallMove", own);
        object usefulPatches = Call(harmony, "GetPatchInfo", useful.GetMethod("OnRun", All));
        Check(((IEnumerable)Get(usefulPatches, "Prefixes")).Cast<object>().Count(p => ((MethodInfo)Get(p, "PatchMethod")).DeclaringType.Name == "MapExtendEffects") == 1, "move prefix provider deduplicates across different assemblies");
        Type module = T("Features.Mechanics.MapExtend.MapExtendModule");
        Check(!(bool)Call(module, "HasProvider"), "fresh map module may load");
        foreach (object h in new[] { foreignOwner, own }) harmony.GetMethod("UnpatchSelf").Invoke(h, null);
    }
}
