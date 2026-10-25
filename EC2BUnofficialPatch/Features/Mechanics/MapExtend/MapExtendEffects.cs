using System;
using System.Collections.Generic;
using System.Linq;
using Config;
using EC2BUnofficialPatch.Core;
using Effect;
using HarmonyLib;

namespace EC2BUnofficialPatch.Features.Mechanics.MapExtend
{
    internal static class MapExtendEffects
    {
        internal static void Install(Harmony harmony)
        {
            InstallMove(harmony);
            PatchOnce(harmony, typeof(EffectorUseful), nameof(EffectorUseful.OnRun), nameof(RunObject));
            PatchOnce(harmony, typeof(EffectorUseful), nameof(EffectorUseful.OnToString), nameof(DescribeObject));
            PatchOnce(harmony, typeof(EffectorUI), nameof(EffectorUI.OnRun), nameof(Run));
            PatchOnce(harmony, typeof(EffectorUI), nameof(EffectorUI.OnToString), nameof(Describe));
        }
        internal static void InstallMove(Harmony harmony)
        {
            PatchOnce(harmony, typeof(EffectorUseful), nameof(EffectorUseful.OnRun), nameof(RunUseful));
            PatchOnce(harmony, typeof(EffectorUseful), nameof(EffectorUseful.OnToString), nameof(DescribeUseful));
        }
        private static void PatchOnce(Harmony harmony, Type type, string method, string prefix)
        {
            var target = AccessTools.Method(type, method);
            // Both distribution variants reuse this class name and prefix contract.
            // Recognize another assembly's installed provider, regardless of load order.
            if (Harmony.GetPatchInfo(target)?.Prefixes.Any(p =>
                p.PatchMethod.DeclaringType?.Name == nameof(MapExtendEffects) && p.PatchMethod.Name == prefix) == true) return;
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(MapExtendEffects), prefix));
        }
        public static bool RunUseful(int ___subType, int ___id)
        {
            if (___subType == 1)
            {
                if (MapMoveService.MoveTo(___id)) PatchLog.Info($"效果模块-100,1地点移动已执行：effect=100,1,{___id}");
                return false;
            }
            return true;
        }
        public static bool RunObject(int ___subType, List<float> ___effect)
        {
            if (___subType != 11) return true;
            if (!SceneObjectEffectState.Read(___effect, out int mode, out int person))
                PatchLog.Warning("地图扩展效果需要100,11,1/-1/2/-2,PersonCfgID。");
            else SceneObjectsRuntime.SetEffect(person, mode);
            return false;
        }
        public static bool DescribeUseful(int ___subType, int ___id, int _type, ref string __result)
        {
            if (___subType == 1) { __result = MapMoveService.GetDescription(___id, _type); return false; }
            return true;
        }
        public static bool DescribeObject(int ___subType, List<float> ___effect, ref string __result)
        {
            if (___subType != 11) return true;
            __result = SceneObjectEffectState.Read(___effect, out int mode, out int person)
                ? (mode == 1 ? "显示物件普通图：" : mode == -1 ? "隐藏物件普通图：" : mode == 2 ? "物件可点击：" : "物件不可点击：") + person
                : "无效的场景物件效果";
            return false;
        }
        public static bool Run(EffectorUI __instance, int ___id)
        {
            if (__instance.type != 5 && __instance.type != -5) return true;
            WorldMapsRuntime.SetWorldUnlocked(___id, __instance.type == 5);
            return false;
        }
        public static bool Describe(EffectorUI __instance, int ___id, ref string __result)
        {
            if (__instance.type != 5 && __instance.type != -5) return true;
            var world = WorldMapsRuntime.Worlds.Find(item => item.Id == ___id);
            __result = (__instance.type == 5 ? "解锁大地图[" : "移除大地图[") + (world?.Name ?? ___id.ToString()) + "]";
            return false;
        }
    }
    internal static class SceneObjectEffectState
    {
        internal static bool Read(List<float> effect, out int mode, out int person)
        {
            mode = person = 0;
            if (effect == null || effect.Count != 4 || effect[0] != 100 || effect[1] != 11 ||
                (effect[2] != 1 && effect[2] != -1 && effect[2] != 2 && effect[2] != -2) ||
                float.IsNaN(effect[3]) || float.IsInfinity(effect[3]) || effect[3] <= 0 || (double)effect[3] > int.MaxValue || effect[3] != Math.Floor(effect[3])) return false;
            mode = (int)effect[2]; person = (int)effect[3]; return true;
        }
        internal static bool HasImage(SceneObjectEntry entry, int mode) =>
            (Math.Abs(mode) == 1 ? !string.IsNullOrEmpty(entry.Geometry.ImagePath) :
                !string.IsNullOrEmpty(entry.Geometry.ImagePath) || !string.IsNullOrEmpty(entry.Geometry.HoverImagePath));
        internal static bool Resolve(SceneObjectEntry entry, bool condition, bool click)
        {
            var states = click ? WorldMapState.ObjectClick : WorldMapState.ObjectNormal;
            return HasImage(entry, click ? 2 : 1) && states.TryGetValue(entry.PersonId, out bool value) ? value : condition;
        }
    }
}
