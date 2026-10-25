using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;

namespace EC2BUnofficialPatch.Core
{
    // Compare content, not timestamps: saving an unchanged file must not refresh UI.
    internal sealed class ReloadInputSnapshot
    {
        private readonly Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        internal static ReloadInputSnapshot Capture(IEnumerable<string> paths, bool includeImages = false)
        {
            var result = new ReloadInputSnapshot();
            foreach (string path in paths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                result.Add(path);
                if (!includeImages || !File.Exists(path)) continue;
                var json = JToken.Parse(File.ReadAllText(path)) as JContainer;
                if (json == null) continue;
                foreach (var property in json.Descendants().OfType<JProperty>())
                {
                    if ((property.Name != "image" && property.Name != "url") || property.Value.Type != JTokenType.String) continue;
                    string relative = (string)property.Value;
                    if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) ||
                        !relative.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                    result.Add(Path.Combine(Path.GetDirectoryName(path), relative));
                }
            }
            return result;
        }

        private void Add(string path)
        {
            path = Path.GetFullPath(path);
            if (!File.Exists(path)) { files[path] = "missing"; return; }
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create()) files[path] = Convert.ToBase64String(sha.ComputeHash(stream));
        }

        internal bool SameAs(ReloadInputSnapshot previous) => previous != null && files.Count == previous.files.Count &&
            files.All(pair => previous.files.TryGetValue(pair.Key, out string value) && value == pair.Value);
    }
}
