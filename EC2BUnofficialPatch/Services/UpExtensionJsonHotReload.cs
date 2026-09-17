using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using Config;
using EC2BUnofficialPatch.Core;
using EC2BUnofficialPatch.Workshop;
using Newtonsoft.Json.Linq;

namespace EC2BUnofficialPatch.Services
{
    /// <summary>
    /// UP 自有 JSON 的统一预检约定。新增 UP JSON 应放在 EC2BUnofficialPatch 目录下，
    /// 并由对应模块通过 JsonHotReloadApi 注册原子重载回调。
    /// </summary>
    internal static class UpExtensionJsonHotReload
    {
        private static readonly object SyncRoot = new object();
        private static Dictionary<string, string> _baseline;
        private static readonly HashSet<string> LegacyFileNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CustomMinigamecfg.json",
            "CustomLyrics.json",
            "CustomScreenLyrcis.json",
            "Custompaper.json",
            "CustomVideo.json",
            "RoleAvailabilityCfg.json"
        };

        internal static void CaptureBaseline(ContentRootCatalog catalog)
        {
            lock (SyncRoot)
                _baseline = BuildSnapshot(catalog, false);
        }

        internal static UpJsonChangeSet Prepare(ContentRootCatalog catalog)
        {
            Dictionary<string, string> current = BuildSnapshot(catalog, true);
            Dictionary<string, string> previous;
            lock (SyncRoot)
                previous = _baseline ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string[] changed = current.Keys
                .Where(path => !previous.TryGetValue(path, out string hash) || hash != current[path])
                .Concat(previous.Keys.Where(path => !current.ContainsKey(path)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new UpJsonChangeSet(current, changed);
        }

        internal static void Commit(UpJsonChangeSet changeSet)
        {
            if (changeSet == null) return;
            lock (SyncRoot)
                _baseline = changeSet.Snapshot;
        }

        private static Dictionary<string, string> BuildSnapshot(
            ContentRootCatalog catalog,
            bool validateSyntax)
        {
            string[] files = EnumerateJsonFiles(catalog)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string file in files)
            {
                try
                {
                    string content = File.ReadAllText(file);
                    if (validateSyntax)
                        JToken.Parse(content);
                    result[file] = Hash(content);
                }
                catch (Exception exception)
                {
                    throw new InvalidDataException(
                        $"UP JSON 解析失败：file={file}, reason={ModuleHost.GetReason(exception)}",
                        exception);
                }
            }

            return result;
        }

        private static string Hash(string content)
        {
            using (SHA256 sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(content ?? string.Empty)));
        }

        private static IEnumerable<string> EnumerateJsonFiles(ContentRootCatalog catalog)
        {
            string local = Path.Combine(Paths.PluginPath, "EC2BUnofficialPatch");
            foreach (string file in EnumerateDirectory(local, "*.json"))
            {
                if (!IsGameCfg(file)) yield return file;
            }

            foreach (ContentRoot root in catalog?.Roots ?? Array.Empty<ContentRoot>())
            {
                if (root == null || !Directory.Exists(root.Path))
                    continue;

                string enhanced = Path.Combine(root.Path, "EC2BUnofficialPatch");
                foreach (string file in EnumerateDirectory(enhanced, "*.json"))
                {
                    if (!IsGameCfg(file)) yield return file;
                }

                // 兼容 1.0.20 以前已经公开的直放路径。
                foreach (string file in EnumerateDirectory(root.Path, "*.json"))
                {
                    if (LegacyFileNames.Contains(Path.GetFileName(file)))
                        yield return file;
                }
            }
        }

        private static bool IsGameCfg(string path)
        {
            string configName = Path.GetFileNameWithoutExtension(path);
            return typeof(Cfg).Assembly.GetType("Config." + configName, false, false) != null;
        }

        private static IEnumerable<string> EnumerateDirectory(string directory, string pattern)
        {
            if (!Directory.Exists(directory))
                yield break;

            string[] files;
            try
            {
                files = Directory.GetFiles(directory, pattern, SearchOption.AllDirectories);
            }
            catch (Exception exception)
            {
                throw new IOException(
                    $"UP JSON 目录扫描失败：path={directory}, reason={exception.Message}",
                    exception);
            }

            foreach (string file in files)
                yield return Path.GetFullPath(file);
        }
    }

    internal sealed class UpJsonChangeSet
    {
        internal UpJsonChangeSet(Dictionary<string, string> snapshot, string[] changedPaths)
        {
            Snapshot = snapshot;
            ChangedPaths = changedPaths ?? Array.Empty<string>();
        }

        internal Dictionary<string, string> Snapshot { get; }
        internal IReadOnlyList<string> ChangedPaths { get; }
        internal int FileCount => Snapshot.Count;
        internal bool HasChanges => ChangedPaths.Count > 0;

        internal string DescribeChanges(int maxPaths = 8)
        {
            string[] shown = ChangedPaths.Take(maxPaths).ToArray();
            string suffix = ChangedPaths.Count > shown.Length
                ? $"，另有{ChangedPaths.Count - shown.Length}个"
                : string.Empty;
            return string.Join("；", shown) + suffix;
        }
    }
}
