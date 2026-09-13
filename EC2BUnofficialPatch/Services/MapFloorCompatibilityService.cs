using System;
using System.Collections.Generic;
using System.Linq;
using Config;
using EC2BUnofficialPatch.Core;

namespace EC2BUnofficialPatch.Services
{
    /// <summary>
    /// 将 MapCfg 的普通“整项覆盖”与父地图 floors 的“多来源累加”分离。
    /// 分层扩展开启时，已经接入关系树的 type == 2 地图才可继续声明下一层。
    /// </summary>
    internal static class MapFloorCompatibilityService
    {
        private static readonly HashSet<string> LoggedInvalidLinks =
            new HashSet<string>(StringComparer.Ordinal);
        private static Dictionary<int, int> _parentByChild = new Dictionary<int, int>();
        private static Dictionary<int, List<int>> _displayFloorsByContainer =
            new Dictionary<int, List<int>>();
        private static HashSet<int> _containers = new HashSet<int>();

        internal static MapFloorRebuildResult Rebuild(
            IReadOnlyDictionary<int, List<int>> baselineFloors,
            IReadOnlyList<MapFloorCfgDocument> documents,
            bool mergeMultipleSources,
            bool allowNested)
        {
            Dictionary<int, MapCfg> live = Cfg.MapCfgMap;
            if (live == null || live.Count == 0)
                return MapFloorRebuildResult.Empty;

            var rollback = live.ToDictionary(
                pair => pair.Key,
                pair => CloneFloors(pair.Value?.floors));

            try
            {
                int declaredAdded = 0;
                if (mergeMultipleSources)
                {
                    var selectedFloors = new Dictionary<int, List<int>>();
                    if (baselineFloors != null)
                    {
                        foreach (KeyValuePair<int, List<int>> pair in baselineFloors)
                            selectedFloors[pair.Key] = CloneFloors(pair.Value);
                    }

                    // 原版对同一 MapCfg.id 采用先到先得；这里只复现胜出项的 floors 基线。
                    var selectedModIds = new HashSet<int>();
                    foreach (MapFloorCfgDocument document in documents ?? Array.Empty<MapFloorCfgDocument>())
                    {
                        foreach (MapFloorCfgEntry entry in document.Entries)
                        {
                            if (selectedModIds.Add(entry.Id))
                                selectedFloors[entry.Id] = CloneFloors(entry.Floors);
                        }
                    }

                    foreach (KeyValuePair<int, MapCfg> pair in live)
                    {
                        if (pair.Value == null) continue;
                        pair.Value.floors = selectedFloors.TryGetValue(pair.Key, out List<int> selected)
                            ? CloneFloors(selected)
                            : CloneFloors(pair.Value.floors);
                    }

                    foreach (MapFloorCfgDocument document in documents ?? Array.Empty<MapFloorCfgDocument>())
                    {
                        foreach (MapFloorCfgEntry entry in document.Entries)
                        {
                            if (!live.TryGetValue(entry.Id, out MapCfg parent) || parent == null ||
                                (!allowNested && parent.type == 2))
                            {
                                continue;
                            }

                            foreach (int floorId in entry.Floors ?? Array.Empty<int>())
                            {
                                if (!IsExistingFloor(live, entry.Id, floorId))
                                {
                                    LogInvalidLink(document, entry.Id, floorId);
                                    continue;
                                }

                                if (AddUnique(parent, floorId)) declaredAdded++;
                            }
                        }
                    }
                }

                // 先从非 type==2 地图向下建立显式关系。只有已经归入关系树的 type==2
                // 才会继续解释自己的 floors，避免把叶子地图中的祖先导航项倒置成子关系。
                MapFloorHierarchy explicitHierarchy = BuildHierarchy(live, allowNested);
                int autoAdded = 0;
                while (true)
                {
                    bool addedOne = false;
                    foreach (KeyValuePair<int, MapCfg> child in live
                        .Where(pair => pair.Value != null && pair.Value.type == 2)
                        .OrderBy(pair => pair.Key))
                    {
                        if (explicitHierarchy.ParentByChild.ContainsKey(child.Key))
                            continue;

                        int parentId = child.Key / 1000;
                        if (!live.TryGetValue(parentId, out MapCfg parent) || parent == null ||
                            (!allowNested && parent.type == 2) ||
                            parent.floors?.Contains(child.Key) == true)
                        {
                            continue;
                        }

                        // 原版楼层栏把父地图自身作为默认场景入口；全新的空列表也保持该行为。
                        if (parent.floors == null || parent.floors.Count == 0)
                            AddUnique(parent, parentId);
                        if (AddUnique(parent, child.Key)) autoAdded++;
                        addedOne = true;
                        break;
                    }

                    if (!addedOne)
                        break;

                    // 每加入一条兜底关系就重新展开显式树，避免把刚接入树中的嵌套声明
                    // 又按 /1000 错误挂到其他地图。
                    explicitHierarchy = BuildHierarchy(live, allowNested);
                }

                foreach (KeyValuePair<int, MapCfg> child in live
                    .Where(pair => pair.Value != null && pair.Value.type == 2)
                    .OrderBy(pair => pair.Key))
                {
                    if (explicitHierarchy.ParentByChild.ContainsKey(child.Key))
                        continue;
                    int parentId = child.Key / 1000;
                    if (!live.TryGetValue(parentId, out MapCfg parent) || parent == null)
                        LogMissingParent(child.Key, parentId);
                }

                NormalizeFloorLists(live);
                MapFloorHierarchy hierarchy = BuildHierarchy(live, allowNested);
                Dictionary<int, List<int>> displayFloors =
                    BuildDisplayFloors(live, hierarchy.ParentByChild);

                int changedParents = live.Count(pair =>
                    !SequenceEqual(rollback[pair.Key], pair.Value?.floors));
                _parentByChild = hierarchy.ParentByChild;
                _containers = new HashSet<int>(hierarchy.ChildrenByParent.Keys);
                _displayFloorsByContainer = displayFloors;
                return new MapFloorRebuildResult(changedParents, declaredAdded, autoAdded);
            }
            catch
            {
                foreach (KeyValuePair<int, List<int>> pair in rollback)
                {
                    if (live.TryGetValue(pair.Key, out MapCfg map) && map != null)
                        map.floors = CloneFloors(pair.Value);
                }
                throw;
            }
        }

        internal static MapCfg ResolveFloorContainer(int mapId, MapCfg current)
        {
            Dictionary<int, MapCfg> maps = Cfg.MapCfgMap;
            if (maps == null)
                return current;

            // 一个地图同时是子地图和容器时，优先展示自己的下一层；叶子地图才回到直接父层。
            HashSet<int> containers = _containers;
            if (containers != null && containers.Contains(mapId))
                return current;

            Dictionary<int, int> reverse = _parentByChild;
            if (reverse != null &&
                reverse.TryGetValue(mapId, out int explicitParent) &&
                maps.TryGetValue(explicitParent, out MapCfg parent) &&
                parent != null)
            {
                return parent;
            }

            // 没有UP反向关系时保留原版 type==2、id/1000 行为，但避免父地图缺失时崩溃。
            if (current != null && current.type == 2 &&
                maps.TryGetValue(mapId / 1000, out MapCfg inferred) && inferred != null)
            {
                return inferred;
            }
            return current;
        }

        internal static List<int> ResolveFloorIds(int mapId, MapCfg current)
        {
            MapCfg container = ResolveFloorContainer(mapId, current);
            if (container == null)
                return null;

            Dictionary<int, List<int>> display = _displayFloorsByContainer;
            return display != null && display.TryGetValue(container.id, out List<int> floors)
                ? floors
                : container.floors;
        }

        private static bool IsExistingFloor(
            IReadOnlyDictionary<int, MapCfg> maps,
            int parentId,
            int floorId)
        {
            if (floorId == parentId) return true;
            return maps.TryGetValue(floorId, out MapCfg child) && child != null;
        }

        private static MapFloorHierarchy BuildHierarchy(
            IReadOnlyDictionary<int, MapCfg> maps,
            bool allowNested)
        {
            var parentByChild = new Dictionary<int, int>();
            var childrenByParent = new Dictionary<int, List<int>>();
            var pending = new Queue<int>();
            var expanded = new HashSet<int>();

            // 原版容器为非 type==2 地图；先登记全部根层，保持原有单层关系优先。
            foreach (KeyValuePair<int, MapCfg> root in maps
                .Where(pair => pair.Value != null && pair.Value.type != 2)
                .OrderBy(pair => pair.Key))
            {
                if (root.Value.floors != null && root.Value.floors.Count > 0)
                    pending.Enqueue(root.Key);
            }

            while (pending.Count > 0)
            {
                int parentId = pending.Dequeue();
                if (!expanded.Add(parentId) ||
                    !maps.TryGetValue(parentId, out MapCfg parent) || parent == null)
                {
                    continue;
                }

                foreach (int childId in parent.floors ?? Enumerable.Empty<int>())
                {
                    if (childId == parentId || !maps.ContainsKey(childId))
                        continue;

                    // floors 可以包含祖先作为“返回上一级”按钮，它不是新的向下关系。
                    if (IsAncestor(childId, parentId, parentByChild))
                        continue;

                    if (parentByChild.TryGetValue(childId, out int existingParent))
                    {
                        if (existingParent != parentId)
                            LogConflictingParents(childId, existingParent, parentId);
                        continue;
                    }

                    if (WouldCreateCycle(parentId, childId, parentByChild))
                    {
                        LogCyclicRelation(parentId, childId);
                        continue;
                    }

                    parentByChild[childId] = parentId;
                    if (!childrenByParent.TryGetValue(parentId, out List<int> children))
                    {
                        children = new List<int>();
                        childrenByParent[parentId] = children;
                    }
                    children.Add(childId);

                    if (allowNested &&
                        maps.TryGetValue(childId, out MapCfg child) &&
                        child?.floors != null && child.floors.Count > 0)
                    {
                        pending.Enqueue(childId);
                    }
                }
            }

            return new MapFloorHierarchy(parentByChild, childrenByParent);
        }

        private static void NormalizeFloorLists(Dictionary<int, MapCfg> maps)
        {
            foreach (KeyValuePair<int, MapCfg> parent in maps)
            {
                if (parent.Value?.floors == null) continue;
                parent.Value.floors = parent.Value.floors
                    .Where(id => id == parent.Key || maps.ContainsKey(id))
                    .Distinct()
                    .ToList();
            }
        }

        private static Dictionary<int, List<int>> BuildDisplayFloors(
            IReadOnlyDictionary<int, MapCfg> maps,
            IReadOnlyDictionary<int, int> parentByChild)
        {
            var result = new Dictionary<int, List<int>>();
            foreach (KeyValuePair<int, MapCfg> container in maps)
            {
                if (container.Value?.floors == null)
                    continue;

                var visible = new List<int>();
                foreach (int floorId in container.Value.floors)
                {
                    if (!maps.ContainsKey(floorId) || visible.Contains(floorId))
                        continue;

                    bool isSelf = floorId == container.Key;
                    bool isAncestor = IsAncestor(floorId, container.Key, parentByChild);
                    bool isDirectChild = parentByChild.TryGetValue(floorId, out int parentId) &&
                                         parentId == container.Key;
                    if (isSelf || isAncestor || isDirectChild)
                        visible.Add(floorId);
                }
                result[container.Key] = visible;
            }
            return result;
        }

        private static bool IsAncestor(
            int possibleAncestor,
            int mapId,
            IReadOnlyDictionary<int, int> parentByChild)
        {
            var visited = new HashSet<int>();
            int current = mapId;
            while (visited.Add(current) &&
                   parentByChild.TryGetValue(current, out int parentId))
            {
                if (parentId == possibleAncestor)
                    return true;
                current = parentId;
            }
            return false;
        }

        private static bool WouldCreateCycle(
            int parentId,
            int childId,
            IReadOnlyDictionary<int, int> parentByChild) =>
            parentId == childId || IsAncestor(childId, parentId, parentByChild);

        private static bool AddUnique(MapCfg parent, int floorId)
        {
            if (parent.floors == null) parent.floors = new List<int>();
            if (parent.floors.Contains(floorId)) return false;
            parent.floors.Add(floorId);
            return true;
        }

        private static List<int> CloneFloors(IEnumerable<int> floors) =>
            floors == null ? null : new List<int>(floors);

        private static bool SequenceEqual(IReadOnlyList<int> left, IReadOnlyList<int> right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.Count != right.Count) return false;
            for (int index = 0; index < left.Count; index++)
                if (left[index] != right[index]) return false;
            return true;
        }

        private static void LogInvalidLink(MapFloorCfgDocument document, int parentId, int floorId)
        {
            string key = document.ModId + "|" + parentId + "|" + floorId;
            if (!LoggedInvalidLinks.Add(key)) return;
            PatchLog.Warning(
                "优化模块-地图子地点兼容已忽略无效 floors 注册：" +
                $"mod={document.ModId}, parent={parentId}, floor={floorId}, " +
                "reason=目标地图不存在");
        }

        private static void LogConflictingParents(int childId, int keptParent, int ignoredParent)
        {
            string key = "conflict|" + childId + "|" + keptParent + "|" + ignoredParent;
            if (!LoggedInvalidLinks.Add(key)) return;
            PatchLog.Warning(
                "优化模块-地图子地点兼容发现同一子地图被多个父地图声明，保留先建立的父关系：" +
                $"child={childId}, keptParent={keptParent}, ignoredParent={ignoredParent}");
        }

        private static void LogCyclicRelation(int parentId, int childId)
        {
            string key = "cycle|" + parentId + "|" + childId;
            if (!LoggedInvalidLinks.Add(key)) return;
            PatchLog.Warning(
                "优化模块-地图子地点扩展已忽略会形成循环的 floors 关系：" +
                $"parent={parentId}, child={childId}");
        }

        private static void LogMissingParent(int childId, int parentId)
        {
            string key = "auto|" + childId + "|" + parentId;
            if (!LoggedInvalidLinks.Add(key)) return;
            PatchLog.Warning(
                "优化模块-地图子地点兼容发现孤立子地图，已跳过自动注册：" +
                $"child={childId}, inferredParent={parentId}, reason=父地图不存在");
        }
    }

    internal sealed class MapFloorHierarchy
    {
        internal MapFloorHierarchy(
            Dictionary<int, int> parentByChild,
            Dictionary<int, List<int>> childrenByParent)
        {
            ParentByChild = parentByChild ?? new Dictionary<int, int>();
            ChildrenByParent = childrenByParent ?? new Dictionary<int, List<int>>();
        }

        internal Dictionary<int, int> ParentByChild { get; }
        internal Dictionary<int, List<int>> ChildrenByParent { get; }
    }

    internal sealed class MapFloorCfgDocument
    {
        internal MapFloorCfgDocument(ulong modId, IReadOnlyList<MapFloorCfgEntry> entries)
        {
            ModId = modId;
            Entries = entries ?? Array.Empty<MapFloorCfgEntry>();
        }

        internal ulong ModId { get; }
        internal IReadOnlyList<MapFloorCfgEntry> Entries { get; }
    }

    internal sealed class MapFloorCfgEntry
    {
        internal MapFloorCfgEntry(int id, IReadOnlyList<int> floors)
        {
            Id = id;
            Floors = floors;
        }

        internal int Id { get; }
        internal IReadOnlyList<int> Floors { get; }
    }

    internal readonly struct MapFloorRebuildResult
    {
        internal static readonly MapFloorRebuildResult Empty = new MapFloorRebuildResult(0, 0, 0);

        internal MapFloorRebuildResult(int changedParents, int declaredAdded, int autoAdded)
        {
            ChangedParents = changedParents;
            DeclaredAdded = declaredAdded;
            AutoAdded = autoAdded;
        }

        internal int ChangedParents { get; }
        internal int DeclaredAdded { get; }
        internal int AutoAdded { get; }
    }
}
