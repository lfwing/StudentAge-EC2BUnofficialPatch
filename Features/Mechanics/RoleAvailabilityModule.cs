using System;
using System.Collections.Generic;
using System.Reflection;
using Config;
using EC2BUnofficialPatch.Core;
using EC2BUnofficialPatch.Services;
using HarmonyLib;
using Sdk;
using TheEntity;

namespace EC2BUnofficialPatch.Features.Mechanics
{
    internal sealed class RoleAvailabilityModule : IPluginModule
    {
        private static readonly IReadOnlyList<ModuleLogItem> Items = new[]
        {
            new ModuleLogItem("机制", "控制角色在列表显示")
        };

        public string Key => "mechanics.role-availability";
        public IReadOnlyList<ModuleLogItem> LogItems => Items;

        public void Load(Harmony harmony, PluginServices services)
        {
            RoleAvailabilityPatches.Initialize(RoleAvailabilityService.Load(services.ContentRoots));
            Patch(harmony, AccessTools.Method(typeof(RelationData), "GetAllSocialNpcs"), postfix: nameof(RoleAvailabilityPatches.SocialListPostfix));
            Patch(harmony, AccessTools.Method(typeof(RelationData), "GetOtherRelation"), postfix: nameof(RoleAvailabilityPatches.OtherRelationPostfix));
            Patch(harmony, AccessTools.Method(typeof(StudyData), "UpdateClassmateEnable"), postfix: nameof(RoleAvailabilityPatches.ClassmateEnablePostfix));
            Patch(
                harmony,
                AccessTools.Method(typeof(StudyData), "InitExamRankWeight"),
                prefix: nameof(RoleAvailabilityPatches.ExamPoolPrefix),
                postfix: nameof(RoleAvailabilityPatches.ExamPoolPostfix),
                finalizer: nameof(RoleAvailabilityPatches.ExamPoolFinalizer));
        }

        private static void Patch(Harmony harmony, MethodBase target, string prefix = null, string postfix = null, string finalizer = null)
        {
            if (target == null)
                throw new MissingMethodException("角色可用性补丁目标不存在");
            harmony.Patch(
                target,
                prefix: prefix == null ? null : new HarmonyMethod(typeof(RoleAvailabilityPatches), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(RoleAvailabilityPatches), postfix),
                finalizer: finalizer == null ? null : new HarmonyMethod(typeof(RoleAvailabilityPatches), finalizer));
        }
    }

    internal static class RoleAvailabilityPatches
    {
        private static RoleAvailabilityService _service;
        private static readonly HashSet<string> LoggedUses = new HashSet<string>(StringComparer.Ordinal);

        internal static void Initialize(RoleAvailabilityService service)
        {
            _service = service;
            LoggedUses.Clear();
        }

        internal static void SocialListPostfix(ref List<int> __result)
        {
            FilterSocial(ref __result, "GetAllSocialNpcs");
        }

        internal static void OtherRelationPostfix(int _type, ref List<int> __result)
        {
            if (_type == -2 || _type == -3)
                FilterSocial(ref __result, $"GetOtherRelation({_type})");
        }

        internal static void ClassmateEnablePostfix(ExamRankWeightData tmp, ref bool __result)
        {
            if (tmp == null || tmp.roleId <= 0 || !_service.IsConfigured(tmp.roleId))
                return;

            Role main = Singleton<RoleMgr>.Ins.GetRole();
            if (main == null || !_service.CanTakeExam(tmp.roleId, main.GradeState, main.ClassType))
            {
                tmp.enable = false;
                __result = false;
                LogUseOnce($"classmate:{tmp.roleId}",
                    $"机制模块-角色列表控制调用：personId={tmp.roleId}, target=考试同学池, result=排除");
            }
        }

        internal static void ExamPoolPrefix(
            int _gradeState,
            int _classType,
            out Dictionary<PersonGrowCfg, List<int>> __state)
        {
            __state = new Dictionary<PersonGrowCfg, List<int>>();
            if (Cfg.PersonGrowCfgMap == null)
                return;

            foreach (int personId in _service.RegisteredIds)
            {
                PersonGrowCfg grow;
                if (!Cfg.PersonGrowCfgMap.TryGetValue(personId, out grow) || grow == null)
                    continue;

                if (_service.CanTakeExam(personId, _gradeState, _classType))
                    continue;

                __state[grow] = grow.examRank;
                // 只在原版构造考试池的调用栈内临时隐藏；磁盘 CFG 和存档均不修改。
                grow.examRank = new List<int>();
                LogUseOnce($"exam:{personId}:{_gradeState}:{_classType}",
                    $"机制模块-角色列表控制调用：personId={personId}, target=考试排名池, " +
                    $"gradeState={_gradeState}, classType={_classType}, result=排除");
            }
        }

        internal static void ExamPoolPostfix(ref Dictionary<PersonGrowCfg, List<int>> __state)
        {
            Restore(ref __state);
        }

        internal static Exception ExamPoolFinalizer(Exception __exception, ref Dictionary<PersonGrowCfg, List<int>> __state)
        {
            Restore(ref __state);
            return __exception;
        }

        private static void FilterSocial(ref List<int> list, string source)
        {
            if (list == null)
                return;
            List<int> removed = list.FindAll(personId => !_service.IsSocialAvailable(personId));
            list = list.FindAll(personId => _service.IsSocialAvailable(personId));
            if (removed.Count > 0)
            {
                string ids = string.Join(",", removed);
                LogUseOnce($"social:{source}:{ids}",
                    $"机制模块-角色列表控制调用：target={source}, result=过滤, personIds={ids}");
            }
        }

        private static void LogUseOnce(string key, string message)
        {
            if (LoggedUses.Add(key))
                PatchLog.Info(message);
        }

        private static void Restore(ref Dictionary<PersonGrowCfg, List<int>> state)
        {
            if (state == null)
                return;
            foreach (KeyValuePair<PersonGrowCfg, List<int>> pair in state)
                pair.Key.examRank = pair.Value;
            state = null;
        }
    }
}
