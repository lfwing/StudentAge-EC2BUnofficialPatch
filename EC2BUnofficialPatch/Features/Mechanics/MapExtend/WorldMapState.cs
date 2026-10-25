using System.Collections.Generic;
using System.Linq;

namespace EC2BUnofficialPatch.Features.Mechanics.MapExtend
{
    // Use native FuncModel containers already included in ISaveLoadValue's union.
    // These independent save keys never alter the player's real FuncData.
    internal static class WorldMapState
    {
        private const string Prefix = "EC2BUnofficialPatch.WorldMaps.v2.";
        internal static readonly Dictionary<int, bool> Unlocked = new Dictionary<int, bool>();
        internal static readonly HashSet<int> InitializedMaps = new HashSet<int>();
        internal static readonly Dictionary<int, int> ActionIds = new Dictionary<int, int>();
        internal static readonly Dictionary<int, bool> ObjectNormal = new Dictionary<int, bool>();
        internal static readonly Dictionary<int, bool> ObjectClick = new Dictionary<int, bool>();

        internal static void Clear()
        {
            Unlocked.Clear(); InitializedMaps.Clear(); ActionIds.Clear(); ObjectNormal.Clear(); ObjectClick.Clear();
        }
        internal static int Load()
        {
            Clear();
            FuncModel worlds = SaveMgrEx.LoadValue<FuncModel>(false, Prefix + "worlds");
            foreach (var row in worlds?.funcData?.funcDict ?? new Dictionary<int, FuncSubData>())
                Unlocked[row.Key] = row.Value.isOpened;
            FuncModel maps = SaveMgrEx.LoadValue<FuncModel>(false, Prefix + "maps");
            foreach (var row in maps?.funcData?.funcDict ?? new Dictionary<int, FuncSubData>())
                InitializedMaps.Add(row.Key);
            FuncModel actions = SaveMgrEx.LoadValue<FuncModel>(false, Prefix + "actions");
            foreach (var row in actions?.funcData?.funcDict ?? new Dictionary<int, FuncSubData>())
                ActionIds[row.Key] = row.Value.id;
            LoadObjectState("object-normal", ObjectNormal);
            LoadObjectState("object-click", ObjectClick);
            return worlds?.mapData?.recordMapId ?? 0;
        }
        internal static void Save(int activeWorld)
        {
            SaveMgrEx.SaveValue(false, Prefix + "worlds", new FuncModel {
                mapData = new MapData { recordMapId = activeWorld },
                funcData = new FuncData { funcDict = Unlocked.ToDictionary(row => row.Key,
                    row => new FuncSubData { id = row.Key, isOpened = row.Value }) } });
            SaveMgrEx.SaveValue(false, Prefix + "maps", new FuncModel {
                funcData = new FuncData { funcDict = InitializedMaps.ToDictionary(id => id,
                    id => new FuncSubData { id = id, isOpened = true }) } });
            SaveMgrEx.SaveValue(false, Prefix + "actions", new FuncModel {
                funcData = new FuncData { funcDict = ActionIds.ToDictionary(row => row.Key,
                    row => new FuncSubData { id = row.Value }) } });
            SaveObjectState("object-normal", ObjectNormal);
            SaveObjectState("object-click", ObjectClick);
        }
        private static void LoadObjectState(string key, Dictionary<int, bool> values)
        {
            FuncModel model = SaveMgrEx.LoadValue<FuncModel>(false, Prefix + key);
            foreach (var row in model?.funcData?.funcDict ?? new Dictionary<int, FuncSubData>()) values[row.Key] = row.Value.isOpened;
        }
        private static void SaveObjectState(string key, Dictionary<int, bool> values) =>
            SaveMgrEx.SaveValue(false, Prefix + key, new FuncModel { funcData = new FuncData {
                funcDict = values.ToDictionary(row => row.Key, row => new FuncSubData { id = row.Key, isOpened = row.Value }) } });
    }
}
