using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using Config;
using EC2BUnofficialPatch.Core;
using EC2BUnofficialPatch.Workshop;
using Newtonsoft.Json;
using Sdk;

namespace EC2BUnofficialPatch.Services
{
    internal sealed class RoleAvailabilityService
    {
        private readonly Dictionary<int, RoleAvailabilityEntry> _entries;
        private readonly HashSet<int> _conflicts;
        private readonly HashSet<string> _loggedExamErrors = new HashSet<string>(StringComparer.Ordinal);

        private RoleAvailabilityService(
            Dictionary<int, RoleAvailabilityEntry> entries,
            HashSet<int> conflicts)
        {
            _entries = entries;
            _conflicts = conflicts;
        }

        internal IEnumerable<int> RegisteredIds => _entries.Keys.Concat(_conflicts);

        internal static RoleAvailabilityService Load(ContentRootCatalog roots)
        {
            Dictionary<int, RoleAvailabilityEntry> entries = new Dictionary<int, RoleAvailabilityEntry>();
            HashSet<int> conflicts = new HashSet<int>();
            List<string> files = new List<string>
            {
                Path.Combine(Paths.PluginPath, "EC2BUnofficialPatch", "RoleAvailabilityCfg.json")
            };

            foreach (ContentRoot root in roots.Roots)
                files.Add(Path.Combine(root.Path, "EC2BUnofficialPatch", "RoleAvailabilityCfg.json"));

            foreach (string file in files.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                RoleAvailabilityDocument document;
                try
                {
                    document = JsonConvert.DeserializeObject<RoleAvailabilityDocument>(File.ReadAllText(file));
                }
                catch (Exception exception)
                {
                    PatchLog.Error($"RoleAvailability-JSON 读取失败：path={file}, reason={ModuleHost.GetReason(exception)}");
                    continue;
                }

                if (document?.roles == null)
                {
                    PatchLog.Warning($"RoleAvailability-缺少 roles 数组，已忽略：path={file}");
                    continue;
                }

                foreach (RoleAvailabilityEntry entry in document.roles)
                {
                    if (entry == null || entry.personId <= 0 || !entry.takeExam.HasValue || !ValidConditions(entry.cond))
                    {
                        PatchLog.Error($"RoleAvailability-非法条目，已忽略：path={file}, personId={entry?.personId ?? 0}；personId、takeExam 和 cond 必须符合格式");
                        continue;
                    }

                    entry.source = file;
                    if (conflicts.Contains(entry.personId) || entries.ContainsKey(entry.personId))
                    {
                        entries.Remove(entry.personId);
                        conflicts.Add(entry.personId);
                        PatchLog.Error($"RoleAvailability-personId 重复占用，冲突项全部禁用：personId={entry.personId}, path={file}");
                        continue;
                    }

                    entries.Add(entry.personId, entry);
                }
            }

            PatchLog.Registration($"机制模块-角色列表控制注册完成：有效角色={entries.Count}, 冲突角色={conflicts.Count}, 配置文件={files.Count(File.Exists)}");
            return new RoleAvailabilityService(entries, conflicts);
        }

        internal bool IsConfigured(int personId) => _entries.ContainsKey(personId) || _conflicts.Contains(personId);

        internal bool IsConditionMet(int personId)
        {
            if (_conflicts.Contains(personId))
                return false;

            RoleAvailabilityEntry entry;
            if (!_entries.TryGetValue(personId, out entry))
                return true;

            try
            {
                return CommonEvtMgr.IsMatchCondition(entry.cond, true);
            }
            catch (Exception exception)
            {
                PatchLog.Error($"RoleAvailability-Condition 执行失败，按不可用处理：personId={personId}, reason={ModuleHost.GetReason(exception)}");
                return false;
            }
        }

        internal bool IsSocialAvailable(int personId) => !IsConfigured(personId) || IsConditionMet(personId);

        internal bool CanTakeExam(int personId, int gradeState, int classType)
        {
            if (_conflicts.Contains(personId))
                return false;

            RoleAvailabilityEntry entry;
            if (!_entries.TryGetValue(personId, out entry))
                return true;
            if (!IsConditionMet(personId) || entry.takeExam != true)
                return false;

            string reason;
            if (ValidateExamData(personId, gradeState, classType, out reason))
                return true;

            string key = personId + ":" + gradeState + ":" + classType + ":" + reason;
            if (_loggedExamErrors.Add(key))
            {
                PatchLog.Error($"RoleAvailability-personId={personId} 配置为可参加考试，但{reason}，已从考试池排除。source={entry.source}");
            }
            return false;
        }

        internal bool ValidateExamData(int personId, int gradeState, int classType, out string reason)
        {
            reason = null;
            if (Cfg.PersonCfgMap == null || !Cfg.PersonCfgMap.ContainsKey(personId))
            {
                reason = "缺少 Person 数据";
                return false;
            }

            if (Singleton<RoleMgr>.Ins.GetRole(personId) == null)
            {
                reason = "缺少运行时 Role 数据";
                return false;
            }

            PersonGrowCfg grow;
            if (Cfg.PersonGrowCfgMap == null || !Cfg.PersonGrowCfgMap.TryGetValue(personId, out grow))
            {
                reason = "缺少 PersonGrow 数据";
                return false;
            }

            int examIndex = gradeState == 2 && classType == 1 ? 2 :
                            gradeState == 2 && classType == 2 ? 3 :
                            gradeState == 1 ? 1 : 0;
            if (grow.examRank == null || grow.examRank.Count <= examIndex)
            {
                reason = $"缺少当前学段 ExamRank（索引 {examIndex}）";
                return false;
            }

            int rank = grow.examRank[examIndex];
            if (rank <= 0)
            {
                reason = $"当前学段 ExamRank 非正数（值 {rank}）";
                return false;
            }

            int classmateCount = GetClassmateCount(gradeState, classType);
            if (classmateCount <= 0 || !HasClassmateRank(gradeState, classType, rank))
            {
                reason = $"缺少与 ExamRank={rank} 对应的 Classmate 数据（当前池大小 {classmateCount}）";
                return false;
            }

            return true;
        }

        private static int GetClassmateCount(int gradeState, int classType)
        {
            if (gradeState == 2 && classType == 2)
                return Cfg.Classmate3LiKeCfgMap?.Count ?? 0;
            if (gradeState == 2)
                return Cfg.Classmate3WenKeCfgMap?.Count ?? 0;
            if (gradeState == 1)
                return Cfg.Classmate2CfgMap?.Count ?? 0;
            return Cfg.ClassmateCfgMap?.Count ?? 0;
        }

        private static bool HasClassmateRank(int gradeState, int classType, int rank)
        {
            if (gradeState == 2 && classType == 2)
                return Cfg.Classmate3LiKeCfgMap != null && Cfg.Classmate3LiKeCfgMap.ContainsKey(rank);
            if (gradeState == 2)
                return Cfg.Classmate3WenKeCfgMap != null && Cfg.Classmate3WenKeCfgMap.ContainsKey(rank);
            if (gradeState == 1)
                return Cfg.Classmate2CfgMap != null && Cfg.Classmate2CfgMap.ContainsKey(rank);
            return Cfg.ClassmateCfgMap != null && Cfg.ClassmateCfgMap.ContainsKey(rank);
        }

        private static bool ValidConditions(List<List<double>> conditions)
        {
            return conditions == null || conditions.All(row => row != null && row.Count > 0);
        }

        private sealed class RoleAvailabilityDocument
        {
            public List<RoleAvailabilityEntry> roles { get; set; }
        }

        private sealed class RoleAvailabilityEntry
        {
            public int personId { get; set; }
            public List<List<double>> cond { get; set; }
            public bool? takeExam { get; set; }
            [JsonIgnore] public string source { get; set; }
        }
    }
}
