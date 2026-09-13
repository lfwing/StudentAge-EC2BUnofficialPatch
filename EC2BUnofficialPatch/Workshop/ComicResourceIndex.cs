using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Config;
using EC2BUnofficialPatch.Core;

namespace EC2BUnofficialPatch.Workshop
{
    internal sealed class ComicResourceIndex
    {
        private static readonly Regex FrameNameRegex = new Regex("^(?<page>[1-9]\\d*)-(?<frame>[1-9]\\d*)$", RegexOptions.Compiled);

        private readonly ExternalResourceResolver _resolver;
        private readonly Dictionary<string, RootComicIndex> _roots;
        private readonly List<ComicScanIssue> _scanIssues;
        private readonly HashSet<string> _reportedResolutionIssues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<int> _validatedCfgs = new HashSet<int>();

        private ComicResourceIndex(ExternalResourceResolver resolver, Dictionary<string, RootComicIndex> roots, List<ComicScanIssue> scanIssues)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _roots = roots;
            _scanIssues = scanIssues;
        }

        internal int DirectoryCount => _roots.Values.Sum(root => root.DirectoryCount);
        internal int ImageCount => _roots.Values.Sum(root => root.Files.Count);
        internal int ConflictCount => _roots.Values.Sum(root => root.Conflicts.Count + root.ConflictedSets.Count);
        internal int InvalidFileCount => _scanIssues.Count;
        internal IReadOnlyList<ComicScanIssue> ScanIssues => _scanIssues;

        internal static ComicResourceIndex Empty(ExternalResourceResolver resolver)
        {
            return new ComicResourceIndex(
                resolver,
                new Dictionary<string, RootComicIndex>(StringComparer.OrdinalIgnoreCase),
                new List<ComicScanIssue>());
        }

        internal static ComicResourceIndex Build(ContentRootCatalog catalog, ExternalResourceResolver resolver)
        {
            Dictionary<string, RootComicIndex> roots = new Dictionary<string, RootComicIndex>(StringComparer.OrdinalIgnoreCase);
            List<ComicScanIssue> issues = new List<ComicScanIssue>();

            foreach (ContentRoot root in catalog.Roots)
            {
                RootComicIndex index = new RootComicIndex(root);
                foreach (string comicDirectory in ExternalResourceResolver.FindDirectoriesNamed(
                    root.Path,
                    "comic",
                    true,
                    (path, exception) => PatchLog.Warning($"屏幕特效模块-4016漫画目录嗅探跳过不可访问目录：path={path}, reason={exception.Message}")))
                {
                    index.DirectoryCount++;
                    ScanComicDirectory(index, comicDirectory, issues);
                }
                roots[root.Id] = index;
            }

            return new ComicResourceIndex(resolver, roots, issues);
        }

        internal bool IsExternalComicUrl(string url)
        {
            return TryParseComicUrl(url, out _, out _, out _);
        }

        internal bool TryResolve(string url, out string fullPath, out string reason)
        {
            fullPath = null;
            reason = null;
            if (!TryParseComicUrl(url, out string modPackageId, out string relativeKey, out _))
                return false;

            string targetRootId = null;
            if (!string.IsNullOrWhiteSpace(modPackageId) &&
                !_resolver.TryResolveWorkshopRootIdByPackageId(modPackageId, out targetRootId, out string packageReason))
            {
                reason = $"无法解析游戏 Mod packageId：packageId={modPackageId}, reason={packageReason}";
                return false;
            }
            if (!string.IsNullOrEmpty(targetRootId))
            {
                if (!_roots.TryGetValue(targetRootId, out RootComicIndex root))
                {
                    reason = $"未找到 packageId={modPackageId} 对应的 Workshop 内容根：root={targetRootId}";
                    return false;
                }

                return TryResolveInRoot(root, relativeKey, out fullPath, out reason);
            }

            List<ResourceCandidate> matches = new List<ResourceCandidate>();
            foreach (RootComicIndex root in _roots.Values)
            {
                if (!_resolver.IsRuntimeRootEligible(root.Root.Id))
                    continue;
                string baseKey = GetBaseKey(relativeKey);
                if (root.Conflicts.Contains(relativeKey) || root.ConflictedSets.Contains(baseKey))
                    continue;
                if (root.Files.TryGetValue(relativeKey, out ResourceCandidate candidate))
                    matches.Add(candidate);
            }

            if (matches.Count == 1)
            {
                fullPath = matches[0].FullPath;
                return true;
            }

            if (matches.Count > 1)
            {
                reason = $"无法确定漫画所属 Mod，同一相对路径在多个 Workshop Mod 中存在：key={relativeKey}, matches={matches.Count}";
                return false;
            }

            reason = $"未在任何 comic 文件夹中找到漫画图片：key={relativeKey}";
            return false;
        }

        internal void ValidateCfg(CGCfg cfg)
        {
            if (cfg == null || cfg.comic == null || cfg.comic.Count == 0)
                return;
            if (!_validatedCfgs.Add(cfg.id))
                return;

            if (cfg.urls == null || cfg.urls.Count == 0)
            {
                PatchLog.Error($"屏幕特效模块-4016漫画配置错误：cg={cfg.id}, reason=urls 为空");
                return;
            }

            foreach (string baseUrl in cfg.urls.Where(url => !string.IsNullOrWhiteSpace(url)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!TryParseComicBaseUrl(baseUrl, out string modPackageId, out string baseRelative))
                    continue; // 官方 Addressables 漫画不属于外置 comic 通道。

                string rootId = null;
                if (!string.IsNullOrWhiteSpace(modPackageId) &&
                    !_resolver.TryResolveWorkshopRootIdByPackageId(modPackageId, out rootId, out string packageReason))
                {
                    PatchLog.Error($"屏幕特效模块-4016漫画配置校验失败：cg={cfg.id}, url={baseUrl}, reason=无法解析 packageId={modPackageId}：{packageReason}");
                    continue;
                }
                RootComicIndex root = null;
                if (!string.IsNullOrEmpty(rootId))
                {
                    if (!_roots.TryGetValue(rootId, out root))
                    {
                        PatchLog.Error($"屏幕特效模块-4016漫画配置校验失败：cg={cfg.id}, url={baseUrl}, reason=未找到 Workshop 内容根 {rootId}");
                        continue;
                    }
                }
                else if (!TryFindUniqueRootForBase(baseRelative, out root, out string selectReason))
                {
                    PatchLog.Error($"屏幕特效模块-4016漫画配置校验失败：cg={cfg.id}, url={baseUrl}, reason={selectReason}；建议 urls 使用 Mods/<packageId>/.../comic/<漫画目录>");
                    continue;
                }

                ValidateBase(cfg, baseUrl, baseRelative, root);
            }
        }

        internal void ReportScanIssues()
        {
            foreach (ComicScanIssue issue in _scanIssues)
            {
                PatchLog.Error($"屏幕特效模块-4016漫画文件不规范：file={issue.FullPath}, reason={issue.Reason}");
            }
            foreach (RootComicIndex root in _roots.Values)
            {
                foreach (string set in root.ConflictedSets.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                {
                    PatchLog.Error($"屏幕特效模块-4016漫画目录冲突：mod={root.Root.Id}, manga={set}；同一漫画子目录分散在多个 comic 文件夹中，已拒绝合并，避免跨目录拼接分镜");
                }
                foreach (string key in root.Conflicts.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                {
                    PatchLog.Error($"屏幕特效模块-4016漫画资源冲突：mod={root.Root.Id}, key={key}；同一 Mod 中存在重复相对路径，已拒绝自动选择");
                }
            }
        }

        internal void ReportResolutionIssueOnce(string url, string reason)
        {
            string key = (url ?? "<null>") + "|" + (reason ?? "<unknown>");
            if (_reportedResolutionIssues.Add(key))
                PatchLog.Error($"屏幕特效模块-4016漫画外置资源解析失败：url={url}, reason={reason}");
        }

        private bool TryFindUniqueRootForBase(string baseRelative, out RootComicIndex selected, out string reason)
        {
            selected = null;
            reason = null;
            string prefix = NormalizeKey(baseRelative).TrimEnd('/') + "/";
            List<RootComicIndex> matches = _roots.Values
                .Where(root => _resolver.IsRuntimeRootEligible(root.Root.Id) &&
                               !root.ConflictedSets.Contains(NormalizeKey(baseRelative)) &&
                               root.Files.Keys.Any(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (matches.Count == 1)
            {
                selected = matches[0];
                return true;
            }
            if (matches.Count == 0)
            {
                reason = $"没有任何 Workshop Mod 的 comic 索引包含目录 {baseRelative}";
                return false;
            }
            reason = $"目录 {baseRelative} 同时存在于 {matches.Count} 个 Workshop Mod，无法安全判断来源";
            return false;
        }

        private static void ValidateBase(CGCfg cfg, string baseUrl, string baseRelative, RootComicIndex root)
        {
            string normalizedBase = NormalizeKey(baseRelative);
            if (root.ConflictedSets.Contains(normalizedBase))
            {
                PatchLog.Error($"屏幕特效模块-4016漫画配置校验失败：cg={cfg.id}, url={baseUrl}, reason=漫画子目录 {normalizedBase} 同时存在于同一 Mod 的多个 comic 文件夹，拒绝跨目录合并分镜");
                return;
            }
            string prefix = normalizedBase.TrimEnd('/') + "/";
            string[] duplicateKeys = root.Conflicts
                .Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (duplicateKeys.Length > 0)
            {
                PatchLog.Error($"屏幕特效模块-4016漫画配置校验失败：cg={cfg.id}, url={baseUrl}, reason=存在重复分镜资源：{string.Join(",", duplicateKeys)}");
                return;
            }

            Dictionary<int, SortedSet<int>> actual = new Dictionary<int, SortedSet<int>>();

            foreach (string key in root.Files.Keys)
            {
                if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                string fileName = key.Substring(prefix.Length);
                if (fileName.IndexOf('/') >= 0)
                    continue;
                if (!TryParseFrameName(fileName, out int page, out int frame))
                    continue;
                if (!actual.TryGetValue(page, out SortedSet<int> frames))
                {
                    frames = new SortedSet<int>();
                    actual.Add(page, frames);
                }
                frames.Add(frame);
            }

            bool valid = true;
            for (int page = 1; page <= cfg.comic.Count; page++)
            {
                int expectedCount = cfg.comic[page - 1];
                actual.TryGetValue(page, out SortedSet<int> frames);
                int actualCount = frames?.Count ?? 0;
                bool sequenceOk = frames != null && frames.SetEquals(Enumerable.Range(1, Math.Max(0, expectedCount)));
                if (expectedCount <= 0 || actualCount != expectedCount || !sequenceOk)
                {
                    valid = false;
                    string actualFrames = frames == null ? "<none>" : string.Join(",", frames);
                    PatchLog.Error($"屏幕特效模块-4016漫画分镜数量错误：cg={cfg.id}, url={baseUrl}, page={page}, comic={expectedCount}, actual={actualCount}, frames={actualFrames}");
                }
            }

            foreach (int extraPage in actual.Keys.Where(page => page < 1 || page > cfg.comic.Count).OrderBy(page => page))
            {
                valid = false;
                PatchLog.Error($"屏幕特效模块-4016漫画存在多余图号：cg={cfg.id}, url={baseUrl}, page={extraPage}, frames={string.Join(",", actual[extraPage])}");
            }

            if (valid)
            {
                PatchLog.Info($"屏幕特效模块-4016漫画配置校验通过：cg={cfg.id}, url={baseUrl}, pages={cfg.comic.Count}, frames={cfg.comic.Sum()}");
            }
        }

        private static bool TryResolveInRoot(RootComicIndex root, string relativeKey, out string fullPath, out string reason)
        {
            fullPath = null;
            reason = null;
            string baseKey = GetBaseKey(relativeKey);
            if (root.ConflictedSets.Contains(baseKey))
            {
                reason = $"同一 Mod 的漫画子目录分散在多个 comic 文件夹中，拒绝合并：mod={root.Root.Id}, manga={baseKey}";
                return false;
            }
            if (root.Conflicts.Contains(relativeKey))
            {
                reason = $"同一 Mod 内存在重复漫画资源：mod={root.Root.Id}, key={relativeKey}";
                return false;
            }
            if (!root.Files.TryGetValue(relativeKey, out ResourceCandidate candidate))
            {
                reason = $"该 Mod 的 comic 文件夹中不存在资源：mod={root.Root.Id}, key={relativeKey}";
                return false;
            }
            fullPath = candidate.FullPath;
            return true;
        }

        private static void ScanComicDirectory(RootComicIndex root, string comicDirectory, ICollection<ComicScanIssue> issues)
        {
            // 不使用 SearchOption.AllDirectories：其中任意一个不可访问子目录都可能让整套漫画扫描中断。
            // 逐层枚举后，单个目录失败只记录该目录，其他漫画仍可继续建立索引。
            foreach (string file in EnumerateComicFilesSafe(comicDirectory, issues))
            {
                string relative = file.Substring(comicDirectory.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Replace('\\', '/');
                string extension = Path.GetExtension(file);
                string withoutExtension = relative.Substring(0, relative.Length - extension.Length);
                string[] segments = withoutExtension.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

                if (!ExternalResourceResolver.HasSupportedExtension(file, ExternalResourceResolver.ImageExtensions))
                {
                    issues.Add(new ComicScanIssue(file, $"comic 文件夹内只读取 PNG/JPG/JPEG；当前扩展名={extension}"));
                    continue;
                }
                if (segments.Length < 2)
                {
                    issues.Add(new ComicScanIssue(file, "漫画图片必须位于 comic/<漫画子文件夹>/ 下，不能直接放在 comic 根目录"));
                    continue;
                }
                if (!TryParseFrameName(segments[segments.Length - 1], out _, out _))
                {
                    issues.Add(new ComicScanIssue(file, "文件名必须严格为 {图号}-{分镜号}，例如 1-1.png"));
                    continue;
                }

                string key = NormalizeKey(withoutExtension);
                string baseKey = GetBaseKey(key);
                if (root.SetOwners.TryGetValue(baseKey, out string ownerDirectory))
                {
                    if (!string.Equals(ownerDirectory, comicDirectory, StringComparison.OrdinalIgnoreCase))
                        root.ConflictedSets.Add(baseKey);
                }
                else
                {
                    root.SetOwners.Add(baseKey, comicDirectory);
                }

                ResourceCandidate candidate = new ResourceCandidate(root.Root.Id, file);
                if (root.Files.TryGetValue(key, out ResourceCandidate selected))
                {
                    if (!string.Equals(selected.FullPath, candidate.FullPath, StringComparison.OrdinalIgnoreCase))
                        root.Conflicts.Add(key);
                    continue;
                }
                root.Files.Add(key, candidate);
            }
        }


        private static IEnumerable<string> EnumerateComicFilesSafe(
            string comicDirectory,
            ICollection<ComicScanIssue> issues)
        {
            Queue<string> pending = new Queue<string>();
            pending.Enqueue(comicDirectory);

            while (pending.Count > 0)
            {
                string current = pending.Dequeue();
                string[] files;
                try
                {
                    files = Directory.EnumerateFiles(current, "*", SearchOption.TopDirectoryOnly)
                        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                }
                catch (Exception exception)
                {
                    issues.Add(new ComicScanIssue(current, "目录中的文件无法枚举：" + exception.Message));
                    continue;
                }

                foreach (string file in files)
                    yield return file;

                string[] children;
                try
                {
                    children = Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly)
                        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                }
                catch (Exception exception)
                {
                    issues.Add(new ComicScanIssue(current, "目录中的子目录无法枚举：" + exception.Message));
                    continue;
                }

                foreach (string child in children)
                    pending.Enqueue(child);
            }
        }

        private static bool TryParseComicUrl(string url, out string modPackageId, out string relativeKey, out string baseRelative)
        {
            modPackageId = null;
            relativeKey = null;
            baseRelative = null;

            string normalized = ExternalResourceResolver.StripPrefixes(url, "Comic");
            if (normalized == null ||
                !ExternalResourceResolver.TryParseGameModResourcePath(normalized, out GameModResourcePath modPath))
            {
                return false;
            }

            modPackageId = modPath.PackageId;

            // 漫画是严格的功能锚点资源：URL 中必须明确包含 comic 目录。
            // Mods/<packageId>/ 是游戏逻辑前缀，不是 Workshop 安装目录中的物理子目录；
            // comic 锚点只在去掉逻辑前缀后的 Workshop 根内相对路径中解析。
            string tail = ExternalResourceResolver.ExtractAfterAnchor(modPath.ContentRelativePath, "comic");
            if (tail == null || string.Equals(tail, modPath.ContentRelativePath, StringComparison.OrdinalIgnoreCase))
                return false;

            tail = ExternalResourceResolver.RemoveSupportedExtension(
                tail,
                ExternalResourceResolver.ImageExtensions);
            relativeKey = NormalizeKey(tail);
            if (string.IsNullOrWhiteSpace(relativeKey))
                return false;

            int slash = relativeKey.LastIndexOf('/');
            baseRelative = slash > 0 ? relativeKey.Substring(0, slash) : string.Empty;
            return true;
        }

        private static bool TryParseComicBaseUrl(string url, out string modPackageId, out string baseRelative)
        {
            modPackageId = null;
            baseRelative = null;
            if (string.IsNullOrWhiteSpace(url))
                return false;
            string probe = url.TrimEnd('/', '\\') + "/1-1";
            return TryParseComicUrl(probe, out modPackageId, out _, out baseRelative);
        }

        private static bool TryParseFrameName(string name, out int page, out int frame)
        {
            page = 0;
            frame = 0;
            Match match = FrameNameRegex.Match(name ?? string.Empty);
            return match.Success &&
                   int.TryParse(match.Groups["page"].Value, out page) &&
                   int.TryParse(match.Groups["frame"].Value, out frame);
        }

        private static string GetBaseKey(string relativeKey)
        {
            string normalized = NormalizeKey(relativeKey);
            int slash = normalized.LastIndexOf('/');
            return slash > 0 ? normalized.Substring(0, slash) : string.Empty;
        }

        private static string NormalizeKey(string value)
        {
            return ExternalResourceResolver.NormalizeRelativePath(value) ?? string.Empty;
        }

        private sealed class RootComicIndex
        {
            internal RootComicIndex(ContentRoot root)
            {
                Root = root;
            }
            internal ContentRoot Root { get; }
            internal int DirectoryCount { get; set; }
            internal Dictionary<string, ResourceCandidate> Files { get; } = new Dictionary<string, ResourceCandidate>(StringComparer.OrdinalIgnoreCase);
            internal Dictionary<string, string> SetOwners { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            internal HashSet<string> ConflictedSets { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            internal HashSet<string> Conflicts { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    internal sealed class ComicScanIssue
    {
        internal ComicScanIssue(string fullPath, string reason)
        {
            FullPath = fullPath;
            Reason = reason;
        }
        internal string FullPath { get; }
        internal string Reason { get; }
    }
}
