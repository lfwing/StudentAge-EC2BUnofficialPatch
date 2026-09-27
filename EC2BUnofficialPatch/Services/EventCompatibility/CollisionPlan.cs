using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EC2BUnofficialPatch.Services.EventCompatibility
{
    internal sealed class ModInput
    {
        public string Id;
        public readonly Dictionary<string, JObject> Tables = new Dictionary<string, JObject>();
    }

    internal sealed class IdRegistry
    {
        public int Version = 1;
        // Persist both original owners and aliases, including disabled/deleted mods.
        public Dictionary<string, string> Owners = new Dictionary<string, string>();
        public Dictionary<string, int> Aliases = new Dictionary<string, int>();
    }

    internal sealed class CollisionPlan
    {
        public static readonly string[] Tables = { "EvtCfg", "TalkCfg", "OptionCfg" };
        public readonly Dictionary<string, Dictionary<string, JObject>> Output = new Dictionary<string, Dictionary<string, JObject>>();
        public readonly List<string> Report = new List<string>();
        public readonly IdRegistry Registry;
        readonly Dictionary<string, HashSet<int>> originals;
        readonly HashSet<string> declarations = new HashSet<string>();
        readonly Dictionary<string, int> mappings = new Dictionary<string, int>();
        readonly Dictionary<string, HashSet<int>> occupied = new Dictionary<string, HashSet<int>>();
        const int MinId = 8000000, MaxId = 15999999; // All IDs remain exactly representable by native float effect arrays.

        public static string Key(string mod, string table, int id) => mod + "/" + table + "/" + id.ToString(CultureInfo.InvariantCulture);
        public static string Slot(string table, int id) => table + "/" + id.ToString(CultureInfo.InvariantCulture);

        public CollisionPlan(IList<ModInput> mods, Dictionary<string, HashSet<int>> baseline, IdRegistry saved)
        {
            originals = baseline;
            Registry = JsonConvert.DeserializeObject<IdRegistry>(JsonConvert.SerializeObject(saved ?? new IdRegistry()));
            if (Registry.Version != 1 || Registry.Owners == null || Registry.Aliases == null) throw new InvalidOperationException("Unsupported or damaged ID registry");
            if (mods.Select(x => x.Id).Distinct().Count() != mods.Count) throw new InvalidOperationException("Duplicate mod identity");
            foreach (var table in Tables)
            {
                if (!baseline.ContainsKey(table)) throw new InvalidOperationException("Missing native baseline: " + table);
                occupied[table] = new HashSet<int>(baseline[table]);
            }
            foreach (var mod in mods)
                foreach (var table in Tables)
                {
                    JObject rows;
                    if (!mod.Tables.TryGetValue(table, out rows)) continue;
                    foreach (var row in rows.Properties())
                    {
                        int id = ParseId(row.Name);
                        if (!(row.Value is JObject) || (int?)row.Value["id"] != id) throw new InvalidOperationException("Key/id mismatch: " + Key(mod.Id, table, id));
                        declarations.Add(Key(mod.Id, table, id)); occupied[table].Add(id);
                    }
                }
            // A new upstream record must never hijack an ID already stored in save histories.
            var usedAliases = new HashSet<string>();
            foreach (var alias in Registry.Aliases)
            {
                var parts = alias.Key.Split('/');
                if (parts.Length != 3 || !occupied.ContainsKey(parts[1]) || alias.Value < MinId || alias.Value > MaxId || !usedAliases.Add(Slot(parts[1], alias.Value)))
                    throw new InvalidOperationException("Invalid alias registry: " + alias.Key);
                if (occupied[parts[1]].Contains(alias.Value)) throw new InvalidOperationException("Reserved ID now declared by game/mod: " + alias.Value);
                occupied[parts[1]].Add(alias.Value);
            }
            foreach (var mod in mods)
                foreach (var table in Tables)
                {
                    JObject rows;
                    if (!mod.Tables.TryGetValue(table, out rows)) continue;
                    foreach (var row in rows.Properties().OrderBy(p => ParseId(p.Name)))
                    {
                        int id = ParseId(row.Name); string key = Key(mod.Id, table, id), slot = Slot(table, id);
                        if (id <= 1 || baseline[table].Contains(id))
                        {
                            if (Registry.Aliases.ContainsKey(key)) throw new InvalidOperationException("Previously remapped ID is now native; migration required: " + key);
                            Report.Add("OVERRIDE " + key); continue;
                        }
                        string owner;
                        if (!Registry.Owners.TryGetValue(slot, out owner)) Registry.Owners[slot] = owner = mod.Id;
                        int runtime;
                        if (Registry.Aliases.TryGetValue(key, out runtime)) mappings[key] = runtime;
                        else if (owner != mod.Id) Registry.Aliases[key] = mappings[key] = runtime = Allocate(table, key);
                        else runtime = id;
                        if (runtime != id) Report.Add("REMAP " + key + " -> " + runtime);
                    }
                }
            foreach (var mod in mods)
            {
                var tables = new Dictionary<string, JObject>(); Output.Add(mod.Id, tables);
                foreach (var table in mod.Tables)
                {
                    var output = new JObject(); tables.Add(table.Key, output);
                    foreach (var row in table.Value.Properties())
                    {
                        var copy = (JObject)row.Value.DeepClone();
                        int id = ParseId(row.Name), runtime = Own(mod.Id, table.Key, id);
                        if (runtime != id) copy["id"] = runtime;
                        ReferenceRules.Rewrite(table.Key, copy, (target, value) => Resolve(mod.Id, target, value));
                        output.Add(runtime.ToString(CultureInfo.InvariantCulture), copy);
                    }
                }
            }
        }

        static int ParseId(string s)
        {
            int id;
            if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) throw new InvalidOperationException("Invalid config ID: " + s);
            return id;
        }

        int Allocate(string table, string key)
        {
            byte[] digest;
            using (var sha = SHA256.Create()) digest = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
            uint seed = (uint)digest[0] | ((uint)digest[1] << 8) | ((uint)digest[2] << 16) | ((uint)digest[3] << 24);
            int span = MaxId - MinId + 1, start = (int)(seed % (uint)span);
            for (int i = 0; i < span; i++)
            {
                int id = MinId + (start + i) % span;
                if (occupied[table].Add(id)) return id;
            }
            throw new InvalidOperationException("No free runtime IDs in " + table);
        }

        public int Own(string mod, string table, int id)
        {
            int value; return mappings.TryGetValue(Key(mod, table, id), out value) ? value : id;
        }

        public int Resolve(string mod, string table, int id)
        {
            if (id <= 0 || !originals.ContainsKey(table) || originals[table].Contains(id)) return id;
            if (declarations.Contains(Key(mod, table, id))) return Own(mod, table, id);
            // Unqualified cross-mod references keep the original global meaning. They never fan out.
            return id;
        }
    }
}
