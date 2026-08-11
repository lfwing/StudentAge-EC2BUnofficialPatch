using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EC2BUnofficialPatch.Core;
using EC2BUnofficialPatch.Services;
using Effect;
using GenUI.Action;
using HarmonyLib;
using Sdk;
using TheEntity;

namespace EC2BUnofficialPatch.Features.Effects
{
    internal sealed class RelationEffectModule : IPluginModule
    {
        private static readonly IReadOnlyList<ModuleLogItem> Items = new[]
        {
            new ModuleLogItem("效果", "20关系效果修复与扩展")
        };

        public string Key => "effects.relation";
        public IReadOnlyList<ModuleLogItem> LogItems => Items;

        public void Load(Harmony harmony, PluginServices services)
        {
            ConstructorInfo constructor = AccessTools.Constructor(
                typeof(EffectorChangeRelation),
                new[] { typeof(Effector), typeof(List<float>) });
            Patch(harmony, constructor, postfix: nameof(RelationEffectPatches.ConstructorPostfix));
            Patch(harmony, AccessTools.Method(typeof(EffectorChangeRelation), "OnRun"), prefix: nameof(RelationEffectPatches.OnRunPrefix));
            Patch(harmony, AccessTools.Method(typeof(EffectorChangeRelation), "OnToString"), prefix: nameof(RelationEffectPatches.OnToStringPrefix));
            Patch(harmony, AccessTools.Method(typeof(StudyData), "UpdateClassmateEnable"), prefix: nameof(RelationEffectPatches.ClassmateEnablePrefix));
            Patch(harmony, AccessTools.Method(typeof(StudyData), "InitExamRankWeight"), postfix: nameof(RelationEffectPatches.ExamPoolPostfix));
            Patch(harmony, AccessTools.Method(typeof(StudyData), "CalcExamRank"), postfix: nameof(RelationEffectPatches.FinalExamRankPostfix));

            Type quickSocial = AccessTools.TypeByName("View.TheAction.QuickSocialView");
            MethodInfo render = quickSocial == null ? null : AccessTools.Method(quickSocial, "OnRenderSocial");
            if (render != null)
                Patch(harmony, render, postfix: nameof(RelationEffectPatches.QuickSocialRenderPostfix));
            else
                PatchLog.Warning("效果模块-找不到 QuickSocialView.OnRenderSocial，新离开状态仍生效但界面文字无法扩展");
        }

        private static void Patch(Harmony harmony, MethodBase target, string prefix = null, string postfix = null)
        {
            if (target == null)
                throw new MissingMethodException("20类关系效果补丁目标不存在");

            harmony.Patch(
                target,
                prefix: prefix == null ? null : new HarmonyMethod(typeof(RelationEffectPatches), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(RelationEffectPatches), postfix));
        }
    }

    internal static class RelationEffectPatches
    {
        private static readonly HashSet<string> LoggedExamExclusions = new HashSet<string>(StringComparer.Ordinal);

        internal static void ConstructorPostfix(List<float> _effect, EffectorChangeRelation __instance, ref int ___npcId)
        {
            if (_effect == null || _effect.Count < 3)
                return;

            int subType = (int)_effect[1];
            if (subType == 520 || subType == -524 || subType == -525)
            {
                __instance.subType = subType;
                ___npcId = (int)_effect[2];
            }
        }

        internal static bool OnRunPrefix(
            EffectorChangeRelation __instance,
            bool _toast,
            int ___npcId,
            int ___mapId)
        {
            int subType = __instance.subType;
            if (subType != -520 && subType != 520 && subType != -521 && subType != 521 &&
                subType != -522 && subType != -523 && subType != -524 && subType != -525)
                return true;

            RelationData relation = Singleton<RoleMgr>.Ins.GetRelationData(true);
            Role role = Singleton<RoleMgr>.Ins.GetRole(___npcId);
            if (role == null)
            {
                PatchLog.Error($"效果模块-关系效果目标不存在：effect=20,{subType}, personId={___npcId}");
                return false;
            }

            int oldRelation = role.Relation;
            bool oldIsLeave = role.isLeave;
            int oldLeaveType = role.leaveType;
            bool applied = true;

            switch (subType)
            {
                case -520:
                    PreserveRelation(role);
                    relation.NPCLeave(___npcId, true, -520);
                    break;
                case -525:
                    if (role.Relation < 0 && role.relationBeforeUnfocus >= 0)
                        relation.ChangeRelation(___npcId, role.relationBeforeUnfocus, null, false);
                    PreserveRelation(role);
                    relation.NPCLeave(___npcId, false, -525);
                    break;
                case 520:
                    if (role.isLeave && (role.leaveType == -520 || role.leaveType == -525))
                    {
                        int restoreRelation = Math.Max(0, role.relationBeforeUnfocus);
                        relation.NPCBack(___npcId, ___mapId);
                        if (role.Relation != restoreRelation)
                            relation.ChangeRelation(___npcId, restoreRelation, null, true);
                    }
                    else
                    {
                        applied = false;
                        PatchLog.Warning($"效果模块-忽略不匹配的 520 恢复：personId={___npcId}, leaveType={role.leaveType}, relation={role.Relation}");
                    }
                    break;
                case -521:
                    relation.NPCLeave(___npcId, false, -521);
                    break;
                case -522:
                case -523:
                case -524:
                    relation.NPCLeave(___npcId, false, subType);
                    break;
                case 521:
                    if (role.isLeave && (role.leaveType == 0 || role.leaveType == -521 || role.leaveType == -522 ||
                                         role.leaveType == -523 || role.leaveType == -524))
                    {
                        relation.NPCBack(___npcId, ___mapId);
                    }
                    else
                    {
                        applied = false;
                        PatchLog.Warning($"效果模块-忽略不匹配的 521 恢复：personId={___npcId}, leaveType={role.leaveType}, relation={role.Relation}");
                    }
                    break;
            }

            RelationFocusCountService.SyncSearchFriendCnt(relation);
            EventMgr.Send(401);
            EventMgr.Send(105);
            if (applied)
            {
                string text = GetEffectText(subType, ___npcId);
                // TalkCfg normally executes effects with _toast=false. Extended leave
                // states still need their own result text, like the original -520 effect.
                if (subType != -520 && subType != 520 && !string.IsNullOrEmpty(text))
                    ToastHelper.Toast(text);

                PatchLog.Info(
                    $"效果模块-20关系效果已执行：effect=20,{subType},{___npcId}, " +
                    $"name={role.Name}, relation={oldRelation}->{role.Relation}, " +
                    $"leave={oldIsLeave}/{oldLeaveType}->{role.isLeave}/{role.leaveType}, toast={_toast}");
            }
            return false;
        }

        internal static bool OnToStringPrefix(EffectorChangeRelation __instance, int ___npcId, ref string __result)
        {
            int subType = __instance.subType;
            if (subType != -520 && subType != 520 && subType != -521 && subType != 521 &&
                subType != -522 && subType != -523 && subType != -524 && subType != -525)
                return true;

            __result = GetEffectText(subType, ___npcId);
            return false;
        }

        internal static void QuickSocialRenderPostfix(UICell _cell)
        {
            Cell_QuickSocialItemUI cell = _cell as Cell_QuickSocialItemUI;
            if (cell == null || cell.data == null)
                return;

            Role role = Singleton<RoleMgr>.Ins.GetRole((int)cell.data);
            if (role == null || !role.isLeave)
                return;

            if (role.leaveType == -520)
                cell.txt_tips.text = "已与你分道扬镳";
            else if (role.leaveType == -524)
                cell.txt_tips.text = "下落不明";
            else if (role.leaveType == -525)
                cell.txt_tips.text = "■■■■";
        }

        internal static void ExamPoolPostfix(List<ExamRankWeightData> ___examRankWeights)
        {
            if (___examRankWeights == null)
                return;

            foreach (ExamRankWeightData entry in ___examRankWeights)
            {
                if (entry == null || entry.roleId <= 0 || !entry.enable)
                    continue;

                Role role = Singleton<RoleMgr>.Ins.GetRole(entry.roleId);
                if (role == null || !role.isLeave || !ShouldExcludeFromExam(role.leaveType))
                    continue;

                // 原版只对 ClassmateCfg 条目调用 UpdateClassmateEnable；通过
                // PersonGrow.examRank 首次插入的具名角色会绕过 isLeave 检查。
                entry.enable = false;
                string key = entry.roleId + ":" + role.leaveType;
                if (LoggedExamExclusions.Add(key))
                {
                    PatchLog.Info(
                        $"效果模块-20关系离开状态已应用于考试池：personId={entry.roleId}, " +
                        $"leaveType={role.leaveType}, result=排除");
                }
            }
        }

        internal static bool ClassmateEnablePrefix(ExamRankWeightData tmp, ref bool __result)
        {
            if (tmp == null || tmp.roleId <= 0)
                return true;

            Role role = Singleton<RoleMgr>.Ins.GetRole(tmp.roleId);
            if (role == null || !role.isLeave)
                return true;

            // 原版会排除所有 isLeave 角色；扩展规则只让四种指定状态失去考试资格。
            bool enabled = CommonEvtMgr.IsMatchCondition(tmp.cond, true) &&
                           !ShouldExcludeFromExam(role.leaveType);
            tmp.enable = enabled;
            __result = enabled;
            return false;
        }

        private static bool ShouldExcludeFromExam(int leaveType)
        {
            // 旧版补丁曾把 -521 保存成 0；保留对已有存档的兼容。
            return leaveType == 0 || leaveType == -521 || leaveType == -522 ||
                   leaveType == -524 || leaveType == -525;
        }

        internal static void FinalExamRankPostfix(StudyData __instance)
        {
            if (__instance?.examRanks == null || __instance.examRanks.Count == 0)
                return;

            List<ExamRankData> removed = __instance.examRanks.FindAll(IsExcludedExamResult);
            if (removed.Count == 0)
                return;

            int oldSelfRank = __instance.examRank?.rank ?? 0;
            __instance.examRanks.RemoveAll(IsExcludedExamResult);
            for (int index = 0; index < __instance.examRanks.Count; index++)
                __instance.examRanks[index].rank = index + 1;

            if (__instance.rankBetterNpcs != null)
            {
                HashSet<int> removedNameIds = new HashSet<int>(removed.Select(item => item.nameId));
                __instance.rankBetterNpcs.RemoveAll(removedNameIds.Contains);
            }

            int newSelfRank = __instance.examRank?.rank ?? oldSelfRank;
            if (newSelfRank != oldSelfRank)
                Singleton<RoleMgr>.Ins.UpdateConditionData(10, 10, newSelfRank, 0);

            string details = string.Join(",", removed.Select(item =>
            {
                Role role = Singleton<RoleMgr>.Ins.GetRole(item.roleId);
                return item.roleId + ":" + (role?.leaveType ?? 0);
            }));
            PatchLog.Info(
                $"效果模块-20关系离开状态已从最终考试排名排除：roles={details}, " +
                $"selfRank={oldSelfRank}->{newSelfRank}, remaining={__instance.examRanks.Count}");
        }

        private static bool IsExcludedExamResult(ExamRankData item)
        {
            if (item == null || item.roleId <= 0)
                return false;

            Role role = Singleton<RoleMgr>.Ins.GetRole(item.roleId);
            return role != null && role.isLeave && ShouldExcludeFromExam(role.leaveType);
        }

        private static void PreserveRelation(Role role)
        {
            if (role.Relation >= 0)
                role.relationBeforeUnfocus = role.Relation;
        }

        private static string GetEffectText(int subType, int npcId)
        {
            string name = RoleMgr.GetRoleName(npcId, PersonNameDefine.Full, null);
            switch (subType)
            {
                case -520: return name + "与你分道扬镳";
                case 520: return name + "恢复与你的社交关系";
                case -521: return name + "离开城市";
                case 521: return name + "回到城市";
                case -522: return name + "休学";
                case -523: return name + "暂时无法联系";
                case -524: return name + "下落不明";
                case -525: return name + "■■■■";
                default: return null;
            }
        }
    }
}
