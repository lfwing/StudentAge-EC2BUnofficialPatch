using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EC2BUnofficialPatch.Workshop
{
    /// <summary>
    /// Only searches inside the supplied Mod root. Wrapper names never determine Mod ownership.
    /// Preferred layout first, direct layout second, other wrappers last. All sources are retained;
    /// each registry keeps its own existing ID conflict policy.
    /// </summary>
    internal static class FeatureContentDiscovery
    {
        internal static readonly string[] FeatureNames =
        {
            "BetterAudio", "comic", "custombadminton", "ExternalLive2D", "LoveDraw",
            "Minigame", "ScreenLyrcis", "ScreenPaper", "ScreenVideo", "MapExtend"
        };

        internal static IEnumerable<string> Directories(string modRoot, string featureName,
            Action<string, Exception> onError = null)
        {
            if (string.IsNullOrWhiteSpace(modRoot) || string.IsNullOrWhiteSpace(featureName))
                return Array.Empty<string>();
            modRoot = Path.GetFullPath(modRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return Descendants(modRoot, onError, featureName)
                .Where(path => string.Equals(Path.GetFileName(path), featureName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => LayoutRank(modRoot, path))
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase);
        }

        internal static IEnumerable<string> ConfigFiles(string modRoot, string featureName, string fileName)
        {
            foreach (string directory in Directories(modRoot, featureName))
            {
                string file = ChildFile(directory, fileName);
                if (file != null) yield return file;
            }
        }

        // Legacy standalone JSON and RoleAvailability have no mandatory feature folder.
        internal static IEnumerable<string> FilesNamed(string modRoot, string fileName)
        {
            if (!Directory.Exists(modRoot)) yield break;
            modRoot = Path.GetFullPath(modRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (string directory in new[] { modRoot }.Concat(Descendants(modRoot))
                .OrderBy(path => LayoutRank(modRoot, Path.Combine(path, fileName)))
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                string file = ChildFile(directory, fileName);
                if (file != null) yield return file;
            }
        }

        internal static string ChildFile(string directory, string name)
        {
            if (!Directory.Exists(directory)) return null;
            try
            {
                return Directory.EnumerateFiles(directory).FirstOrDefault(path =>
                    string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase));
            }
            catch { return null; }
        }

        internal static string RelativeFile(string directory, string value)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(directory)) return null;
            string relative = value.Trim().Replace('\\', '/');
            if (Path.IsPathRooted(relative) || relative.Contains(":")) return null;
            if (relative.Split('/').Any(part => part == "." || part == "..")) return null;
            string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string result = Path.GetFullPath(Path.Combine(root, relative));
            return result.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? result : null;
        }

        private static int LayoutRank(string root, string path)
        {
            string parent = Path.GetDirectoryName(path);
            if (string.Equals(parent, Path.Combine(root, "EC2BUnofficialPatch"), StringComparison.OrdinalIgnoreCase)) return 0;
            return string.Equals(parent, root, StringComparison.OrdinalIgnoreCase) ? 1 : 2;
        }

        private static IEnumerable<string> Descendants(string root, Action<string, Exception> onError = null,
            string stopAtName = null)
        {
            if (!Directory.Exists(root)) yield break;
            var pending = new Queue<string>();
            pending.Enqueue(Path.GetFullPath(root));
            while (pending.Count > 0)
            {
                string current = pending.Dequeue();
                string[] children;
                try
                {
                    // Do not follow junctions outside the owning Mod or into recursive loops.
                    children = Directory.EnumerateDirectories(current)
                        .Where(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
                        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
                }
                catch (Exception exception)
                {
                    onError?.Invoke(current, exception);
                    continue;
                }
                foreach (string child in children)
                {
                    yield return child;
                    if (!string.Equals(Path.GetFileName(child), stopAtName, StringComparison.OrdinalIgnoreCase))
                        pending.Enqueue(child);
                }
            }
        }
    }
}
