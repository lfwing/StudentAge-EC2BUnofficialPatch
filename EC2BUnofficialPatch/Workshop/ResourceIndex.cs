using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;

namespace EC2BUnofficialPatch.Workshop
{
    internal sealed class ResourceIndex
    {
        private readonly ExternalResourceResolver _resolver;
        private readonly Dictionary<string, List<ResourceCandidate>> _loveDrawImages;
        private readonly Dictionary<string, List<ResourceCandidate>> _loveDrawVideos;
        private readonly List<ResourceConflict> _conflicts;
        private readonly IReadOnlyList<string> _lyricFiles;
        private readonly IReadOnlyList<string> _loveDrawDirectories;

        private ResourceIndex(
            ExternalResourceResolver resolver,
            Dictionary<string, List<ResourceCandidate>> loveDrawImages,
            Dictionary<string, List<ResourceCandidate>> loveDrawVideos,
            List<ResourceConflict> conflicts,
            IReadOnlyList<string> lyricFiles,
            IReadOnlyList<string> loveDrawDirectories)
        {
            _resolver = resolver;
            _loveDrawImages = loveDrawImages;
            _loveDrawVideos = loveDrawVideos;
            _conflicts = conflicts;
            _lyricFiles = lyricFiles;
            _loveDrawDirectories = loveDrawDirectories;
        }

        internal IReadOnlyList<ResourceConflict> Conflicts => _conflicts;
        internal IReadOnlyList<string> LyricFiles => _lyricFiles;
        internal IReadOnlyList<string> LoveDrawDirectories => _loveDrawDirectories;

        internal int LoveDrawImageCount => _loveDrawImages.Values
            .SelectMany(candidates => candidates)
            .Select(candidate => candidate.FullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        internal int LoveDrawVideoCount => _loveDrawVideos.Values
            .SelectMany(candidates => candidates)
            .Select(candidate => candidate.FullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        internal static ResourceIndex Build(ContentRootCatalog catalog, ExternalResourceResolver resolver)
        {
            Dictionary<string, List<ResourceCandidate>> loveDrawImages =
                new Dictionary<string, List<ResourceCandidate>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<ResourceCandidate>> loveDrawVideos =
                new Dictionary<string, List<ResourceCandidate>>(StringComparer.OrdinalIgnoreCase);
            List<ResourceConflict> conflicts = new List<ResourceConflict>();
            List<string> lyricFiles = new List<string>();
            List<string> loveDrawDirectories = new List<string>();

            foreach (ContentRoot root in catalog.Roots)
            {
                CollectLyricFiles(root, lyricFiles);

                foreach (string directory in EnumerateDirectFeatureDirectories(root.Path, "LoveDraw"))
                {
                    loveDrawDirectories.Add(directory);
                    RegisterLoveDrawDirectory(
                        root.Id,
                        directory,
                        loveDrawImages,
                        loveDrawVideos,
                        conflicts);
                }
            }

            // 插件自身也可以提供 LoveDraw 外置资源。它没有 Workshop packageId，
            // 因此只参与“未指定 Mod 且唯一命中”的解析。
            foreach (string directory in EnumerateLocalPluginLoveDrawDirectories())
            {
                loveDrawDirectories.Add(directory);
                RegisterLoveDrawDirectory(
                    "plugin-local",
                    directory,
                    loveDrawImages,
                    loveDrawVideos,
                    conflicts);
            }

            return new ResourceIndex(
                resolver,
                loveDrawImages,
                loveDrawVideos,
                conflicts,
                lyricFiles,
                loveDrawDirectories.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }

        internal bool TryResolveLoveDrawImage(string configuredPath, out string fullPath)
        {
            return TryResolveLoveDrawImage(configuredPath, out fullPath, out _);
        }

        internal bool TryResolveLoveDrawImage(string configuredPath, out string fullPath, out string reason)
        {
            return TryResolveLoveDrawResource(
                configuredPath,
                _loveDrawImages,
                ExternalResourceResolver.ImageExtensions,
                out fullPath,
                out reason);
        }

        internal bool TryResolveLoveDrawVideo(string configuredPath, out string fullPath)
        {
            return TryResolveLoveDrawVideo(configuredPath, out fullPath, out _);
        }

        internal bool TryResolveLoveDrawVideo(string configuredPath, out string fullPath, out string reason)
        {
            return TryResolveLoveDrawResource(
                configuredPath,
                _loveDrawVideos,
                ExternalResourceResolver.VideoExtensions,
                out fullPath,
                out reason);
        }

        internal static bool LooksLikeExplicitLoveDrawPath(string configuredPath)
        {
            if (!TryNormalizeLoveDrawRequest(configuredPath, out string modPackageId, out string normalized))
                return false;

            return !string.IsNullOrWhiteSpace(modPackageId) ||
                   normalized.IndexOf('/') >= 0 ||
                   ExternalResourceResolver.HasSupportedExtension(
                       normalized,
                       ExternalResourceResolver.ImageExtensions.Concat(ExternalResourceResolver.VideoExtensions));
        }

        private bool TryResolveLoveDrawResource(
            string configuredPath,
            IReadOnlyDictionary<string, List<ResourceCandidate>> resources,
            IEnumerable<string> extensions,
            out string fullPath,
            out string reason)
        {
            fullPath = null;
            reason = null;

            if (!TryNormalizeLoveDrawRequest(configuredPath, out string modPackageId, out string key))
            {
                reason = "路径为空、包含 .. 或无法规范化";
                return false;
            }

            string targetRootId = null;
            if (!string.IsNullOrWhiteSpace(modPackageId))
            {
                if (!_resolver.TryResolveWorkshopRootIdByPackageId(modPackageId, out targetRootId, out string packageReason))
                {
                    reason = $"无法解析游戏 Mod packageId：packageId={modPackageId}, reason={packageReason}";
                    return false;
                }
            }

            List<string> lookupKeys = new List<string> { key };
            if (string.IsNullOrEmpty(Path.GetExtension(key)))
            {
                lookupKeys.AddRange((extensions ?? Array.Empty<string>()).Select(extension => key + extension));
            }

            foreach (string lookupKey in lookupKeys.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!resources.TryGetValue(lookupKey, out List<ResourceCandidate> allCandidates))
                    continue;

                ResourceCandidate[] candidates = allCandidates
                    .Where(candidate => string.IsNullOrEmpty(targetRootId)
                        ? _resolver.IsRuntimeRootEligible(candidate.SourceId)
                        : string.Equals(candidate.SourceId, targetRootId, StringComparison.OrdinalIgnoreCase))
                    .GroupBy(candidate => candidate.FullPath, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToArray();

                if (candidates.Length == 1)
                {
                    fullPath = candidates[0].FullPath;
                    return true;
                }

                if (candidates.Length > 1)
                {
                    reason = string.IsNullOrEmpty(targetRootId)
                        ? $"同一相对路径在 {candidates.Length} 个资源位置存在，无法安全判断来源：key={lookupKey}；建议使用 Mods/<packageId>/.../LoveDraw/..."
                        : $"同一 Mod 内存在重复 LoveDraw 资源：mod={targetRootId}, key={lookupKey}";
                    return false;
                }
            }

            reason = string.IsNullOrEmpty(targetRootId)
                ? $"未在 LoveDraw 外置资源索引中找到：key={key}"
                : $"未在指定 Mod 的 LoveDraw 目录中找到：mod={targetRootId}, key={key}";
            return false;
        }

        private static bool TryNormalizeLoveDrawRequest(
            string configuredPath,
            out string modPackageId,
            out string normalized)
        {
            modPackageId = null;
            normalized = ExternalResourceResolver.StripPrefixes(
                configuredPath,
                "assets/res/textures/paint",
                "assets/res/videos/paint",
                "textures/paint",
                "videos/paint");
            if (normalized == null)
                return false;

            if (!ExternalResourceResolver.TryParseGameModResourcePath(normalized, out GameModResourcePath modPath))
                return false;

            modPackageId = modPath.PackageId;
            normalized = ExternalResourceResolver.ExtractAfterAnchor(modPath.ContentRelativePath, "LoveDraw");
            return normalized != null;
        }

        private static void RegisterLoveDrawDirectory(
            string sourceId,
            string directory,
            IDictionary<string, List<ResourceCandidate>> images,
            IDictionary<string, List<ResourceCandidate>> videos,
            ICollection<ResourceConflict> conflicts)
        {
            if (!Directory.Exists(directory))
                return;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception exception)
            {
                Core.PatchLog.Warning(
                    $"底层服务模块-LoveDraw 目录扫描失败：path={directory}, reason={exception.Message}");
                return;
            }

            foreach (string file in files)
            {
                string extension = Path.GetExtension(file);
                IDictionary<string, List<ResourceCandidate>> target;
                if (ExternalResourceResolver.HasSupportedExtension(file, ExternalResourceResolver.ImageExtensions))
                    target = images;
                else if (ExternalResourceResolver.HasSupportedExtension(file, ExternalResourceResolver.VideoExtensions))
                    target = videos;
                else
                    continue;

                string suffix = file.Substring(directory.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Replace('\\', '/');
                ResourceCandidate candidate = new ResourceCandidate(sourceId, file);

                RegisterLoveDrawAlias(suffix, candidate, target, conflicts);
                string withoutExtension = suffix.Substring(0, suffix.Length - extension.Length);
                RegisterLoveDrawAlias(withoutExtension, candidate, target, conflicts);
            }
        }

        private static void RegisterLoveDrawAlias(
            string relativePath,
            ResourceCandidate candidate,
            IDictionary<string, List<ResourceCandidate>> target,
            ICollection<ResourceConflict> conflicts)
        {
            string key = ExternalResourceResolver.NormalizeRelativePath(relativePath);
            if (key == null)
                return;

            if (!target.TryGetValue(key, out List<ResourceCandidate> candidates))
            {
                candidates = new List<ResourceCandidate>();
                target.Add(key, candidates);
            }

            if (candidates.Any(item => string.Equals(item.FullPath, candidate.FullPath, StringComparison.OrdinalIgnoreCase)))
                return;

            if (candidates.Count > 0)
                conflicts.Add(new ResourceConflict("LoveDraw/" + key, candidates[0], candidate));

            candidates.Add(candidate);
        }

        private static void CollectLyricFiles(ContentRoot root, ICollection<string> result)
        {
            AddFileIfExists(
                Path.Combine(root.Path, "EC2BUnofficialPatch", "ScreenLyrcis", "CustomScreenLyrcis.json"),
                result);
            AddFileIfExists(
                Path.Combine(root.Path, "ScreenLyrcis", "CustomScreenLyrcis.json"),
                result);
            AddFileIfExists(Path.Combine(root.Path, "CustomLyrics.json"), result);

            string cfgDirectory = Path.Combine(root.Path, "Cfgs");
            if (!Directory.Exists(cfgDirectory))
                return;

            foreach (string file in Directory
                .GetFiles(cfgDirectory, "CustomLyrics.json", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(file);
            }
        }

        private static IEnumerable<string> EnumerateDirectFeatureDirectories(string modRoot, string featureName)
        {
            string pluginDirectory = ExternalResourceResolver.FindChildDirectory(modRoot, "EC2BUnofficialPatch");
            if (pluginDirectory != null)
            {
                string enhanced = ExternalResourceResolver.FindChildDirectory(pluginDirectory, featureName);
                if (enhanced != null)
                    yield return enhanced;
            }

            string direct = ExternalResourceResolver.FindChildDirectory(modRoot, featureName);
            if (direct != null)
                yield return direct;
        }

        private static IEnumerable<string> EnumerateLocalPluginLoveDrawDirectories()
        {
            string pluginDirectory = ExternalResourceResolver.FindChildDirectory(Paths.PluginPath, "EC2BUnofficialPatch");
            if (pluginDirectory == null)
                yield break;

            string loveDraw = ExternalResourceResolver.FindChildDirectory(pluginDirectory, "LoveDraw");
            if (loveDraw != null)
                yield return loveDraw;
        }

        private static void AddFileIfExists(string path, ICollection<string> result)
        {
            if (File.Exists(path))
                result.Add(path);
        }
    }

    internal sealed class ResourceCandidate
    {
        internal ResourceCandidate(string sourceId, string fullPath)
        {
            SourceId = sourceId;
            FullPath = fullPath;
        }

        internal string SourceId { get; }
        internal string FullPath { get; }
    }

    internal sealed class ResourceConflict
    {
        internal ResourceConflict(string relativePath, ResourceCandidate selected, ResourceCandidate ignored)
        {
            RelativePath = relativePath;
            Selected = selected;
            Ignored = ignored;
        }

        internal string RelativePath { get; }
        internal ResourceCandidate Selected { get; }
        internal ResourceCandidate Ignored { get; }
    }
}
