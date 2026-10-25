using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using Condition;
using Config;
using Effect;
using HarmonyLib;
using Increase;
using MessagePack;
using MessagePack.Resolvers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sdk;
using TheEntity;

internal static partial class EffectExtensionCheck
{
    private const BindingFlags All = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static Assembly up;
    private static RoleModel model;
    private static readonly List<string> passed = new List<string>();
    private static Type T(string name) => up.GetType("EC2BUnofficialPatch." + name, true);
    private static object Call(string type, string method, params object[] args) => AccessTools.Method(T(type), method).Invoke(null, args);
    private static void Set(object target, string name, object value)
    {
        var property = target.GetType().GetProperty(name, All);
        if (property != null) property.SetValue(target, value);
        else AccessTools.Field(target.GetType(), name).SetValue(target, value);
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        passed.Add(message);
    }
    private static void Equal(float actual, float expected, string message) => Check(Math.Abs(actual - expected) < .0005f, message + " => " + actual);
    private static Role Reset()
    {
        model = new RoleModel();
        var role = new Role();
        model.roleDict[0] = role;
        Set(Singleton<RoleMgr>.Ins, "model", model);
        return role;
    }
    private static Effector E(params float[] args) => CommonEvtMgr.GenEffector(args.ToList(), null, 0, 0);
    private static Effector Run(params float[] args) { var e = E(args); e.Run(1, false); return e; }
    private static Conditioner C(params double[] args) => CommonEvtMgr.GenConditioner(args.ToList(), null);
    private static bool NoRecords() => false;
    private static bool OpenFunction(ref bool __result) { __result = true; return false; }

    public static int Main(string[] args)
    {
        var roots = new[] { Path.Combine(args[4], "clr-runtime"), Path.GetDirectoryName(args[0]), Path.Combine(args[1], "StudentAge_Data", "Managed"), args[2] };
        AppDomain.CurrentDomain.AssemblyResolve += (sender, request) => {
            string filename = new AssemblyName(request.Name).Name + ".dll";
            string path = roots.Select(r => Path.Combine(r, filename)).FirstOrDefault(File.Exists);
            return path == null ? null : Assembly.LoadFrom(path);
        };
        Assembly.LoadFrom(Path.Combine(args[4], "clr-runtime", "UnityEngine.CoreModule.dll"));
        try { return Execute(args); }
        catch (Exception error)
        {
            Console.Error.WriteLine("Checks before failure: " + passed.Count + "; last: " + passed.LastOrDefault());
            while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Execute(string[] args)
    {
        up = Assembly.LoadFrom(args[0]);
        var paths = typeof(Paths).GetMethod("SetExecutablePath", All);
        var pathArgs = paths.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : null).ToArray();
        pathArgs[0] = Path.Combine(args[4], "StudentAge.exe"); paths.Invoke(null, pathArgs);
        var textRows = JObject.Parse(File.ReadAllText(Path.Combine(args[3], "TextCfg.json"))).Properties()
            .ToDictionary(p => int.Parse(p.Name), p => new TextCfg { id = int.Parse(p.Name), text = (string)p.Value["text"] });
        typeof(Cfg).GetProperty("TextCfgMap").SetValue(null, textRows);
        foreach (string name in new[] { "PersonAttrCfg", "PersonCfg", "ColorCfg", "PersonConstCfg", "OtherAttrCfg", "TraitsCfg" })
        {
            var property = typeof(Cfg).GetProperty(name + "Map");
            property.SetValue(null, JsonConvert.DeserializeObject(File.ReadAllText(Path.Combine(args[3], name + ".json")), property.PropertyType));
        }
        Set(Singleton<ProfileMgr>.Ins, "model", new ProfileModel());
        var fixture = new Harmony("qa.effects.fixture");
        fixture.Patch(AccessTools.Method(typeof(RecordMgr), "AddRecord"), prefix: new HarmonyMethod(typeof(EffectExtensionCheck), nameof(NoRecords)));
        fixture.Patch(AccessTools.Method(typeof(FuncMgr), "IsFuncOpened"), prefix: new HarmonyMethod(typeof(EffectExtensionCheck), nameof(OpenFunction)));

        // Reproduce the actual native defects, before installing UP's new patches.
        Role role = Reset();
        string native11 = E(1, 11, 361, 1).OnToString(1, 1);
        Run(1, 1001, 5, 361, 2, 2);
        role.UpdateAttr2(361, 5);
        Equal(role.GetAttr(2), 0, "native UpdateAttr2(361) never notifies 1001");
        role.UpdateAttr(361, 1);
        Equal(role.GetAttr(2), 2, "native 1001 ignores the 5-point threshold");

        SetupTraitFixture(fixture);
        var fix = new Harmony("qa.effects.attribute");
        T("Features.Effects.AttributeEffectModule").GetMethod("Load").Invoke(Activator.CreateInstance(T("Features.Effects.AttributeEffectModule"), true), new object[] { fix, null });
        var ext = new Harmony("qa.effects.extensions");
        Call("Features.Effects.NewEffectModule", "InstallRuntime", ext);
        Call("Features.Effects.TraitSelectionPatches", "Install", ext);
        foreach (var target in new[] { AccessTools.Method(typeof(View.Main.DetailSocialView), "SetTrait"),
            AccessTools.Method(typeof(View.Main.DetailSocialView), "OnRenderTrait"),
            AccessTools.Method(typeof(View.Common.RewardSelectView), "OnRender"),
            AccessTools.Method(typeof(View.Common.RewardSelectView), "OnOpen") })
            Check(Harmony.GetPatchInfo(target).Owners.Contains(ext.Id), "trait-specific hook installed: " + target.Name);
        var conditions = new Harmony("qa.conditions.extensions");
        T("Features.Effects.ConditionExtensionModule").GetMethod("Load").Invoke(Activator.CreateInstance(T("Features.Effects.ConditionExtensionModule"), true), new object[] { conditions, null });

        role = Reset();
        foreach (int type in new[] { 0, 1, 2, 3 })
        {
            string mood = E(1, 97, 361, 80, 0, .1f).OnToString(1, type);
            Check(mood.Contains("心情") && mood.Contains("10%") && mood.Contains("获取率"), "97 mood beneficiary and percentage, mode " + type);
            Check(E(1, 97, 361, 80, 2, -.1f).OnToString(1, type).Contains("-10%"), "97 negative percent mode " + type);
        }
        Check(E(1, 11, 361, 1).OnToString(1, 1) == native11, "11 description unchanged");
        Check(E(1, 97, 361, 80, 2, .1f).OnToString(.5f, 1).Contains("5%"), "97 non-unit description rate");
        var threshold = Run(1, 97, 361, 80, 2, .1f);
        foreach (float source in new[] { 79f, 80f, 81f })
        {
            role.SetAttr(361, source);
            Equal(role.GetRealAddValue(2, 0, 10, 1, 2), source < 80 ? 10 : 11, "97 boundary " + source);
        }
        role.RemoveEffect(threshold.GetBaseIncreaserUids());
        Equal(role.GetRealAddValue(2, 0, 10, 1, 2), 10, "97 UID removal");

        role = Reset();
        var linked = Run(1, 1001, 5, 361, 2, 2);
        role.UpdateAttr2(361, 1); Equal(role.GetAttr(2), 0, "1001 sub-threshold increment held");
        role.UpdateAttr2(361, 4); Equal(role.GetAttr(2), 2, "1001 cumulative threshold fires from UpdateAttr2");
        role.UpdateAttr(361, 10); Equal(role.GetAttr(2), 6, "1001 multiple thresholds fire once from UpdateAttr");
        role.SetAttr(361, 19); Equal(role.GetAttr(2), 6, "1001 direct SetAttr keeps remainder");
        MessagePackSerializer.DefaultOptions = ContractlessStandardResolverAllowPrivate.Options;
        byte[] carry = MessagePackSerializer.Serialize(role.IncCtrl);
        Set(role, "IncCtrl", MessagePackSerializer.Deserialize<RoleIncreaserCtrl>(carry));
        role.SetAttr(361, 20); Equal(role.GetAttr(2), 8, "1001 saved remainder restored");
        float? originalMax = Cfg.PersonAttrCfgMap[361].max;
        Cfg.PersonAttrCfgMap[361].max = 20;
        role.UpdateAttr(361, 100); Equal(role.GetAttr(2), 8, "1001 ignores clipped source gain");
        Cfg.PersonAttrCfgMap[361].max = originalMax;
        role.RemoveEffect(linked.GetBaseIncreaserUids());
        role.UpdateAttr(361, 5); Equal(role.GetAttr(2), 8, "1001 restored UID can be removed");
        Check(E(1, 1001, 5, 361, 2, 2).OnToString(1, 1).Contains("情商"), "1001 describes target rather than source twice");

        role = Reset();
        var rate = Run(1,1145,1, 361, 10, 2, 5, 20);
        foreach (float source in new[] { 0f, 9f, 10f, 39f, 40f, 200f })
        {
            role.SetAttr(361, source);
            float bonus = Math.Min(.2f, (float)Math.Floor(source / 10) * .05f);
            Equal(role.GetRealAddValue(2, 0, 10, 1, 2), 10 * (1 + bonus), "102 steps/cap " + source);
        }
        Equal(role.GetRealAddValue(2, 0, -10, 1, 2), -10, "102 only modifies gains");
        Check(rate.OnToString(1, 1).Contains("最高20%"), "102 description percent cap");
        Set(role, "IncCtrl", MessagePackSerializer.Deserialize<RoleIncreaserCtrl>(MessagePackSerializer.Serialize(role.IncCtrl)));
        Equal(role.GetRealAddValue(2, 0, 10, 1, 2), 12, "102 gain survives native controller serialization");
        role.RemoveEffect(rate.GetBaseIncreaserUids());
        Equal(role.GetRealAddValue(2, 0, 10, 1, 2), 10, "102 UID removal");
        Run(1,1145,1, 361, 10, 2, 5, 0);
        Equal(role.GetRealAddValue(2, 0, 10, 1, 2), 10, "102 zero cap grants zero");
        Check(E(1,1145,1, 361, 0, 2, 5, 20).ToString().Contains("此项不生效"), "102 zero step rejected without passive residue");
        Check(E(1,1145,1, 361).ToString().Contains("此项不生效") && E(1, 97, 361) == null && E(1, 1001, 1, 999999, 2, 2) == null, "invalid extension arrays/IDs do not reach unsafe native constructors");

        role = Reset(); role.SetAttr(0, 100);
        var reduce = Run(999,1145, 1, 0, 20);
        Run(1, 1, 0, -5); Equal(role.GetAttr(0), 96, "standard effect -5 becomes -4");
        role.UpdateAttr2(0, -5); Equal(role.GetAttr(0), 92, "UpdateAttr2 reduced exactly once");
        role.SetAttr(0, 87); Equal(role.GetAttr(0), 88, "direct SetAttr negative delta reduced");
        Equal(role.GetRealAddValue(0, 0, 5, 1, 2), 5, "cost reduction leaves gains alone");
        var extraLoss = new IncreaserAttr(null) { attrId = 0, value = -1 };
        role.AddEffect(RoleIncType.AttrAddInc, 0, extraLoss, null);
        role.UpdateAttr(0, -5); Equal(role.GetAttr(0), 83.2f, "final negative AttrAddInc is included before reduction");
        role.RemoveEffect(extraLoss.uid);
        var reduce2 = Run(999,1145, 1, 0, 30);
        Equal(role.GetRealAddValue(0, 0, -10, 1, 2), -5, "same attribute reductions add rather than multiply");
        role.RemoveEffect(reduce2.GetBaseIncreaserUids());
        Equal(role.GetRealAddValue(0, 0, -10, 1, 2), -8, "independent reducer removal");
        model.valueviewData = new ValueviewData();
        model.valueviewData.groups[1] = new ValueviewLineData();
        for (int i = 0; i < 10; i++) model.valueviewData.groups[1].nodes.Add(new ValueviewNodeData { srcId = i + 1, mode = i % 2 + 1, state = ValueviewNodeState.Enable });
        var dynamic = Run(999,1145, 2, 0, 4);
        Equal(role.GetRealAddValue(0, 0, -10, 1, 2), -4, "modified worldview counts both modes dynamically");
        Run(999,1145, 1, 0, 60);
        Equal(role.GetRealAddValue(0, 0, -10, 1, 2), 0, "user example 80% plus ten times 4% caps at 100%");
        model.valueviewData.groups[1].nodes[0].state = ValueviewNodeState.Default;
        model.valueviewData.groups[1].nodes[1].mode = 0;
        Equal((int)Call("Features.Effects.NewEffectPatches", "ModifiedWorldviewCount"), 8, "unmodified/forgotten nodes excluded");
        Set(role, "IncCtrl", MessagePackSerializer.Deserialize<RoleIncreaserCtrl>(MessagePackSerializer.Serialize(role.IncCtrl)));
        Equal(role.GetRealAddValue(0, 0, -10, 1, 2), 0, "custom numeric enum buckets survive save/load");
        role.RemoveEffect(dynamic.GetBaseIncreaserUids());
        Equal(role.GetRealAddValue(0, 0, -10, 1, 2), -2, "dynamic reducer removed after save/load");
        role = Reset(); role.SetAttr(7, 8); Run(999,1145, 1, 7, 20);
        Check(Singleton<RoleMgr>.Ins.HasEnoughCost(7, 10), "affordability uses reduced energy cost");
        Check(Singleton<RoleMgr>.Ins.Cost(7, 10, null, false), "cost transaction accepts reduced amount");
        Equal(role.GetAttr(7), 0, "cost transaction deducts exactly eight");
        var shorthand = E(999,1145, 0, 20);
        shorthand.Run();
        Check(shorthand.ToString(1, 1).Contains("此项不生效") && (shorthand.GetBaseIncreaserUids()?.Count ?? 0) == 0,
            "four-item shorthand remains rejected with visible diagnostic and no passive");
        role = Reset(); role.SetAttr(361, 20); Run(999,1145, 1, 361, 20);
        role.UpdateAttr(361, -5); Equal(role.GetAttr(361), 15, "consume=0 attribute is not discounted");
        Check(E(999,1145,1,361,20).ToString(1,1).Contains("consume为1或2") &&
            E(999,1145,2,361,20).ToString(1,1).Contains("consume为1或2"), "both reducer modes require a consumable attribute");
        role.AddEffect((RoleIncType)195050,361,new IncreaserAttr(null) {attrId=361,value=.2f},null);
        Set(role,"IncCtrl",MessagePackSerializer.Deserialize<RoleIncreaserCtrl>(MessagePackSerializer.Serialize(role.IncCtrl)));
        Equal(role.GetRealAddValue(361,0,-10,1,2),-10,"legacy saved reducer on non-consumable attribute is inert");
        int oldConsume = Cfg.PersonAttrCfgMap[361].consume;
        Cfg.PersonAttrCfgMap[361].consume = 1;
        Check((bool)Call("Features.Effects.NewEffectPatches","ValidSpecial",new List<float>{999,1145,1,361,20}),
            "custom attribute may opt into consume=1");
        Equal(role.GetRealAddValue(361,0,-10,1,2),-8,"runtime consumable flag permits custom attribute reduction");
        Cfg.PersonAttrCfgMap[361].consume = oldConsume;

        // Attribute IDs come from the FOURTH item. Cover the real consume=1/2 CFG,
        // including IDs that are unrelated to the three effect modes.
        foreach (var attr in Cfg.PersonAttrCfgMap.Values.Where(a => a.consume == 1 || a.consume == 2).ToList())
        {
            role = Reset(); role.SetAttr(attr.id, 100);
            float before = role.GetAttr(attr.id);
            Check(before >= 10, "consumable fixture has enough points: " + attr.id);
            Run(999,1145, 1, attr.id, 20);
            Run(1, 1, attr.id, -5);
            Equal(role.GetAttr(attr.id), before - 4, "consume=" + attr.consume + " fixed reduction for attr " + attr.id);
            model.valueviewData = new ValueviewData(); model.valueviewData.groups[1] = new ValueviewLineData();
            model.valueviewData.groups[1].nodes.Add(new ValueviewNodeData {srcId=1, mode=1, state=ValueviewNodeState.Enable});
            model.valueviewData.groups[1].nodes.Add(new ValueviewNodeData {srcId=2, mode=2, state=ValueviewNodeState.Enable});
            Run(999,1145, 2, attr.id, 5);
            Check(Singleton<RoleMgr>.Ins.Cost(attr.id, 10, null, false), "native Cost accepts consumable " + attr.id);
            Equal(role.GetAttr(attr.id), before - 11, "native Cost combines modes 1/2 for attr " + attr.id);
            Run(999,1145, 1, attr.id, 100);
            Singleton<RoleMgr>.Ins.Cost(attr.id, 10, null, false);
            Equal(role.GetAttr(attr.id), before - 11, "100 percent cap for consumable " + attr.id);
        }
        Check(E(999,1145,7,0,10).ToString(1,1).Contains("第4项才是personAttrId"), "wrong mode diagnostic distinguishes attribute position");
        Check(E(999,1145,1,999999,20).ToString(1,1).Contains("第4项personAttrId"), "invalid attribute diagnostic identifies attribute field");
        Check(E(999,1145,1,7,-1).ToString(1,1).Contains("第5项"), "invalid percentage diagnostic does not blame mode");

        role = Reset(); var coefficient = Run(999,1145, 3, 15);
        var nativeMood = new IncreaserOther(null) { otherAttrId = 605, value = 10 };
        role.SetAttr(0, 400); Equal(nativeMood.GetValue(), .46f, "400 mood with +15% coefficient yields +46%");
        role.SetAttr(0, -300); Equal(nativeMood.GetValue(), -.255f, "-300 mood with +15% coefficient yields -25.5%");
        Run(999,1145, 3, 200); Equal(nativeMood.GetValue(), 0, "negative mood never turns into a positive bonus");
        role.SetAttr(0, 400); Equal(nativeMood.GetValue(), 1.26f, "positive coefficient can exceed 100% and adds across sources");

        role = Reset();
        foreach (var pair in new[] { Tuple.Create(3, 150f), Tuple.Create(102, 210f), Tuple.Create(105, 300f) })
        {
            var npc = new Role(); Set(npc, "id", pair.Item1); Set(npc, "Favor", pair.Item2); Set(npc, "Relation", 2);
            model.roleDict[pair.Item1] = npc;
        }
        Check(!C(7, 2, 102, 3).IsMatch(), "user's 102 rank 3 example fails");
        Check(C(7, 2, 102, 2).IsMatch(), "102 rank 2 matches");
        Set(model.roleDict[3], "Favor", 210f);
        Check(C(7, 2, 3, 2).IsMatch() && C(7, 2, 102, 2).IsMatch(), "tied favor shares competition rank");
        Check(!C(7, 2, 0, 1).IsMatch() && !C(7, 2, 999999, 1).IsMatch(), "main role/absent role are not rank candidates");
        Check(!C(7, 2, 102, 0).IsMatch() && !C(7, 2, 102, 1.5).IsMatch(), "rank must be a positive integer");
        var rankChain = CommonEvtMgr.GenConditioner(new List<List<double>> { new List<double> {7,2,102,2}, new List<double> {7,2,105,1} });
        Check(rankChain.IsMatch(), "extended conditions retain native AND chaining");
        Singleton<ModCtrl>.Ins.activeMods = new List<ulong>();
        var mod = C(101, 100, 3672553808d);
        Check(!mod.IsMatch(), "disabled 10-digit mod fails");
        Equal(mod.GetProgress().Item1, 0, "disabled mod prerequisite progress is false");
        Singleton<ModCtrl>.Ins.activeMods.Add(3672553808UL);
        Check(mod.IsMatch(), "enabled 10-digit mod matches exactly");
        Equal(mod.GetProgress().Item1, 1, "enabled mod prerequisite progress is true");
        Check(mod.ToString(1).Contains("3672553808"), "mod prerequisite description preserves all ten digits");
        Check(!C(101, 100, (double)(float)3672553808d).IsMatch(), "nearby float-rounded ID does not match");
        var loadedCondition = MessagePackSerializer.Deserialize<Conditioner>(MessagePackSerializer.Serialize(mod));
        Check(loadedCondition.IsMatch(), "raw double mod ID survives conditioner serialization");
        Check(!C(101, 100, 3672553808.5).IsMatch(), "fractional mod ID rejected");
        Check(!C(101, 100, 9007199254740992d).IsMatch(), "outside exact integer range rejected");
        Singleton<ModCtrl>.Ins.activeMods.Add(9999999999UL);
        Check(C(101, 100, 9999999999d).IsMatch(), "full 10-digit range retained");
        var jsonRows = JsonConvert.DeserializeObject<List<List<double>>>("[[101,100,3672553808]]");
        Check(CommonEvtMgr.GenConditioner(jsonRows).IsMatch(), "JSON double path preserves original mod ID");

        for (int count = 0; count <= 5; count++)
        {
            var input = Enumerable.Range(10, count).ToList();
            var slots = (List<int>)Call("Features.Effects.TraitSelectionPatches", "Slots", input);
            Check(slots.Count == (count >= 4 ? 4 : 3), "trait count " + count + " maps to correct visible slots");
            Check(input.Count == count && slots.Take(Math.Min(count, 4)).SequenceEqual(input.Take(4)), "trait source order/count unchanged " + count);
        }
        foreach (float width in new[] { 960f, 1280f, 1920f, 2560f }) foreach (int count in new[] { 3, 4 })
        {
            float scale = (float)Call("Features.Effects.TraitSelectionPatches", "FitScale", width, 600f, 420f, 550f, count);
            Check(scale > 0 && scale <= 1 && 420 * count * scale * 1.04f <= width + .001f, "card widths fit " + width + "/" + count);
        }
        role = Reset();
        var traitNpc = new Role(); Set(traitNpc, "id", 990103); Set(traitNpc, "Relation", 6);
        Set(traitNpc, "Traits", new List<int> {99010301, 99010302, 99010303, 99010304});
        model.roleDict[990103] = traitNpc;
        Cfg.PersonCfgMap[990103] = new PersonCfg { id = 990103, name = "验证角色", init = new List<int> {2} };
        foreach (int i in Enumerable.Range(1, 4)) Cfg.TraitsCfgMap[99010300 + i] = new TraitsCfg {
            id = 99010300 + i, name = "验证特质" + i,
            effect = new List<List<float>> { new List<float> {999,1145,1,0,i == 1 ? 80 : 10} } };
        traitNpc.SetTrait(99010301, true);
        Equal(role.GetRealAddValue(0, 0, -10, 1, 2), -2, "native SetTrait installs the selected reducer");
        traitNpc.SetTrait(99010304, true);
        Equal(role.GetRealAddValue(0, 0, -10, 1, 2), -9, "selecting fourth trait removes old effect and installs only new one");
        traitNpc.SetTrait(99010304, true);
        Equal(role.GetRealAddValue(0, 0, -10, 1, 2), -9, "reselecting the same fourth trait does not duplicate effect");
        Check(traitNpc.Traits.Count == 4 && traitNpc.Trait == 99010304, "native unlocked trait list and single active trait are preserved");
        // Exercise BOTH native trait description paths, not just OnToString directly.
        Cfg.TraitsCfgMap[99010305] = new TraitsCfg { id = 99010305, name = "无效配置复现",
            effect = new List<List<float>> { new List<float> {999,1145,7,0,10} } };
        traitNpc.SetTrait(99010305, true);
        Equal(role.GetRealAddValue(0, 0, -10, 1, 2), -10, "reported subtype 7 grants no trait reduction");
        var socialView = new View.Main.DetailSocialView(); Set(socialView, "role", traitNpc);
        var traitRows = (System.Collections.IEnumerable)AccessTools.Method(typeof(View.Main.DetailSocialView), "GetTraitEffect").Invoke(socialView, new object[] {1,5});
        var descriptions = traitRows.Cast<object>().Select(row => (string)AccessTools.Field(row.GetType(), "txt").GetValue(row)).ToList();
        Check(descriptions.Count == 2 && descriptions[1].Contains("999,1145,7,0,10") && descriptions[1].Contains("此项不生效"),
            "native social trait detail exposes reported invalid CFG instead of blank text");
        var combined = new List<List<float>> { new List<float> {1,1145,1,361,10,2,2,20},
            new List<float> {999,1145,1,0,20}, new List<float> {999,1145,2,0,4}, new List<float> {999,1145,3,15} };
        Cfg.TraitsCfgMap[99010306] = new TraitsCfg { id = 99010306, name = "标准新效果特质", effect = combined };
        role.SetAttr(361, 20); role.SetAttr(0, 400);
        model.valueviewData = new ValueviewData(); model.valueviewData.groups[1] = new ValueviewLineData();
        for (int i = 0; i < 2; i++) model.valueviewData.groups[1].nodes.Add(new ValueviewNodeData {srcId=i+1, mode=i+1, state=ValueviewNodeState.Enable});
        traitNpc.SetTrait(99010306, true);
        string cardText = CommonEvtMgr.ToEffectorStr(combined, null, 0, 0, 1, 0, null);
        Check(cardText.Contains("最高20%") && cardText.Contains("减少20%") && cardText.Contains("减少4%") && cardText.Contains("额外提升15%"),
            "native selection-card path includes every standard new effect");
        traitRows = (System.Collections.IEnumerable)AccessTools.Method(typeof(View.Main.DetailSocialView), "GetTraitEffect").Invoke(socialView, new object[] {1,5});
        descriptions = traitRows.Cast<object>().Select(row => (string)AccessTools.Field(row.GetType(), "txt").GetValue(row)).ToList();
        Check(descriptions.Count == 5 && descriptions.All(s => !string.IsNullOrEmpty(s)), "native social trait detail includes all four standard new effects");
        Equal(role.GetRealAddValue(0, 0, -10, 1, 2), -7.2f, "native SetTrait combines fixed and worldview reductions on protagonist");
        Equal(role.GetRealAddValue(2, 0, 100, 1, 2), 104, "native SetTrait applies 102 gain rate on protagonist");
        Equal(new IncreaserOther(null) {otherAttrId=605, value=10}.GetValue(), .46f, "native SetTrait applies mood coefficient on protagonist");
        traitNpc.SetTrait(99010305, true);
        Equal(role.GetRealAddValue(0, 0, -10, 1, 2), -10, "switching trait removes both reducers");
        Equal(role.GetRealAddValue(2, 0, 100, 1, 2), 100, "switching trait removes 102 gain rate");
        Equal(new IncreaserOther(null) {otherAttrId=605, value=10}.GetValue(), .4f, "switching trait removes mood coefficient");
        // This trait's first effect uses the Mod's custom attribute, absent in cfg_ref.
        Cfg.PersonAttrCfgMap[524] = JsonConvert.DeserializeObject<PersonAttrCfg>(
            "{\"id\":524,\"name\":[\"纯粹\"],\"min\":0,\"max\":999,\"tag\":13,\"consume\":0,\"order\":524,\"color\":[],\"sprite\":[],\"onlyValueInUI\":0,\"uiType\":0}");
        Cfg.TraitsCfgMap[99010307] = new TraitsCfg {id=99010307, name="纯粹配置复现", effect=new List<List<float>> {
            new List<float> {1,11,524,2}, new List<float> {1,1001,1,361,1,0} } };
        traitNpc.SetTrait(99010307, true);
        role.UpdateAttr2(361, 5);
        Equal(role.GetAttr(0), 405, "current pure trait CFG links actual 361 growth to protagonist mood");
        Check(CommonEvtMgr.ToEffectorStr(Cfg.TraitsCfgMap[99010307].effect).Contains("心情"), "current pure trait CFG has linked-effect description");
        traitNpc.SetTrait(99010305, true); role.UpdateAttr2(361, 5);
        Equal(role.GetAttr(0), 405, "switching away removes pure trait 1001 UID");
        var nativeDialog = (BaseView)Activator.CreateInstance(T("Features.Effects.TraitDescriptionView"), true);
        Check(nativeDialog is View.Hint.CommonComfirmView && nativeDialog.prefabPath == "Prefabs/UI/Hint/CommonConfirmView@Hint",
            "full trait description reuses native confirm view and exact prefab");
        Check(!(bool)Call("Features.Effects.TraitSelectionPatches", "HasOtherSlotProvider"), "UP owns slots when no third party provider exists");
        var other = new Harmony("com.lyy.traits4");
        other.Patch(AccessTools.Method(typeof(View.Main.DetailSocialView), "SetTrait"), prefix: new HarmonyMethod(typeof(EffectExtensionCheck), nameof(NoRecords)));
        Check((bool)Call("Features.Effects.TraitSelectionPatches", "HasOtherSlotProvider"), "known third party slot patch detected by owner");
        other.UnpatchSelf();
        var config = new ConfigFile(Path.Combine(args[4], "sa.EC2B.UnofficialPatch.cfg"), false, new BepInPlugin("sa.EC2B.UnofficialPatch", "EC2BUnofficialPatch",
            (string)T("Core.PluginMetadata").GetField("Version", All).GetRawConstantValue()));
        T("Core.PluginConfig").GetMethod("Initialize", All).Invoke(null, new object[] { config });
        string output = File.ReadAllText(config.ConfigFilePath);
        Check(output.Contains("1属性改变修复与扩展 = true") && output.Contains("新效果扩展 = true"), "effect toggles emitted");
        Check(output.IndexOf("[前提]") < output.IndexOf("新前提扩展 = true") && output.IndexOf("新前提扩展 = true") < output.IndexOf("[指令]"), "condition toggle in reserved prerequisite section");
        string disabledPath = Path.Combine(args[4], "disabled-extensions.cfg");
        File.WriteAllText(disabledPath, "[效果]\n1属性改变修复与扩展 = false\n新效果扩展 = false\n[前提]\n新前提扩展 = false\n[Debug]\n控制台调取历史记录 = false\n");
        var disabled = new ConfigFile(disabledPath, false);
        T("Core.PluginConfig").GetMethod("Initialize", All).Invoke(null, new object[] { disabled });
        foreach (string property in new[] { "AttributeEffects", "NewEffects", "NewConditions", "ConsoleHistory" })
        {
            var entry = T("Core.PluginConfig").GetProperty(property, All).GetValue(null);
            Check(!(bool)entry.GetType().GetProperty("Value").GetValue(entry), "explicit disabled configuration retained: " + property);
        }

        VerifyVisibilityAndConsole(args[4]);
        VerifyTraitMechanics(fixture);

        ext.UnpatchSelf();
        Call("Features.Effects.InsightEffectPatches","CleanupBorrowed",Singleton<RoleMgr>.Ins);
        Equal(Singleton<RoleMgr>.Ins.GetRole().GetRealAddValue(2,0,100,1,2),100,"disabled-module load cleanup removes serialized native borrowed effects");
        var inert = new IncreaserOther { otherAttrId = -1950102, value2 = .1f, value3 = .2f, format = "10" };
        Equal(inert.GetValue(), 0, "serialized extension marker is inert without extension patches");
        Check(loadedCondition.IsMatch(), "condition module remains independent of new effects switch");
        conditions.UnpatchSelf(); fix.UnpatchSelf(); fixture.UnpatchSelf();
        Directory.CreateDirectory(args[4]);
        File.WriteAllText(Path.Combine(args[4], "result.json"), JsonConvert.SerializeObject(new {
            checks = passed.Count, passed = true, cases = passed,
            scope = "Actual game methods/Harmony hooks/MessagePack with a workspace-only Unity CoreModule CLR fixture (System.Math and disabled logging; other ECalls throw). Trait hook installation and layout arithmetic checked; Unity rendering/input not executed." }, Formatting.Indented));
        Console.WriteLine("PASS: " + passed.Count + " actual effect/condition/serialization/layout-contract checks.");
        return 0;
    }
}
