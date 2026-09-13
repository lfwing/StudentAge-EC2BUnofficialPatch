using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EC2BUnofficialPatch.Core;
using EC2BUnofficialPatch.Workshop;
using Newtonsoft.Json;

namespace EC2BUnofficialPatch.Features.ScreenEffects
{
    internal sealed class ScreenPaperDatabase
    {
        public List<ScreenPaperEntry> papers { get; set; } = new List<ScreenPaperEntry>();
    }

    internal sealed class ScreenPaperEntry
    {
        public int id { get; set; }
        public string image { get; set; }
    }

    internal sealed class RegisteredScreenPaper
    {
        internal RegisteredScreenPaper(int id, string imagePath, string sourcePath)
        {
            Id = id;
            ImagePath = imagePath;
            SourcePath = sourcePath;
        }

        internal int Id { get; }
        internal string ImagePath { get; }
        internal string SourcePath { get; }
    }

    internal sealed class ScreenPaperRegistry
    {
        private static readonly string[] SupportedExtensions = { ".png", ".jpg", ".jpeg" };

        private readonly Dictionary<int, RegisteredScreenPaper> _papers =
            new Dictionary<int, RegisteredScreenPaper>();
        private readonly Dictionary<int, string> _claims = new Dictionary<int, string>();
        private readonly HashSet<int> _conflictedIds = new HashSet<int>();
        private readonly HashSet<int> _invalidOriginalIds = new HashSet<int>();
        private readonly HashSet<string> _reportedBrokenImages =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _originalIdsValidated;

        internal int Count => _papers.Count;

        internal static ScreenPaperRegistry Load(IReadOnlyList<ContentRoot> roots)
        {
            var registry = new ScreenPaperRegistry();
            foreach (string configPath in EnumerateConfigFiles(roots))
            {
                registry.LoadFile(configPath);
            }

            return registry;
        }

        internal void ValidateOriginalIds(ICollection<int> originalIds)
        {
            if (_originalIdsValidated || originalIds == null)
            {
                return;
            }

            _originalIdsValidated = true;
            var available = new HashSet<int>(originalIds);
            foreach (RegisteredScreenPaper paper in _papers.Values.ToArray())
            {
                if (available.Contains(paper.Id))
                {
                    continue;
                }

                _papers.Remove(paper.Id);
                _invalidOriginalIds.Add(paper.Id);
                PatchLog.Error(
                    $"5001屏幕纸条扩展-配置错误：Custompaper.json 的 id={paper.Id} " +
                    $"未在当前原版 PaperCfg 中注册，已禁用该图片覆盖。source={paper.SourcePath}");
            }
        }

        internal bool TryGet(int id, out RegisteredScreenPaper paper)
        {
            if (_conflictedIds.Contains(id) || _invalidOriginalIds.Contains(id))
            {
                paper = null;
                return false;
            }

            return _papers.TryGetValue(id, out paper);
        }

        internal void ReportBrokenImage(RegisteredScreenPaper paper)
        {
            if (paper == null || !_reportedBrokenImages.Add(paper.ImagePath))
            {
                return;
            }

            PatchLog.Error(
                $"5001屏幕纸条扩展-图片无法读取，已回退原版纸条图片：" +
                $"id={paper.Id}, image={paper.ImagePath}, source={paper.SourcePath}");
        }

        private void LoadFile(string configPath)
        {
            ScreenPaperDatabase database;
            try
            {
                database = JsonConvert.DeserializeObject<ScreenPaperDatabase>(
                    File.ReadAllText(configPath));
            }
            catch (Exception exception)
            {
                PatchLog.Error(
                    $"5001屏幕纸条扩展-无法读取配置：path={configPath}, reason={exception.Message}");
                return;
            }

            if (database?.papers == null)
            {
                PatchLog.Error(
                    $"5001屏幕纸条扩展-配置错误：根对象缺少 papers 数组。path={configPath}");
                return;
            }

            string directory = Path.GetDirectoryName(Path.GetFullPath(configPath));
            foreach (ScreenPaperEntry entry in database.papers)
            {
                RegisterEntry(entry, configPath, directory);
            }
        }

        private void RegisterEntry(ScreenPaperEntry entry, string configPath, string directory)
        {
            if (entry == null || entry.id <= 0)
            {
                PatchLog.Error(
                    $"5001屏幕纸条扩展-配置错误：papers 项必须包含正整数 id。source={configPath}");
                return;
            }

            if (_conflictedIds.Contains(entry.id))
            {
                PatchLog.Error(
                    $"5001屏幕纸条扩展-ID 冲突：id={entry.id} 已被多个 Custompaper.json 占用，" +
                    $"所有同 ID 自定义图片均已禁用。ignored={configPath}");
                return;
            }

            if (_claims.TryGetValue(entry.id, out string firstSource))
            {
                _papers.Remove(entry.id);
                _conflictedIds.Add(entry.id);
                PatchLog.Error(
                    $"5001屏幕纸条扩展-ID 冲突：id={entry.id}, first={firstSource}, " +
                    $"second={configPath}。为避免跨 Mod 抢占，已回退原版图片。");
                return;
            }

            _claims.Add(entry.id, configPath);

            if (string.IsNullOrWhiteSpace(entry.image))
            {
                PatchLog.Warning(
                    $"5001屏幕纸条扩展-id={entry.id} 未填写 image，将使用原版图片。source={configPath}");
                return;
            }

            if (!TryResolveImage(directory, entry.image, out string imagePath, out string reason))
            {
                PatchLog.Warning(
                    $"5001屏幕纸条扩展-id={entry.id} 的 image 无效，将使用原版图片：" +
                    $"reason={reason}, source={configPath}");
                return;
            }

            var candidate = new RegisteredScreenPaper(entry.id, imagePath, configPath);
            _papers.Add(entry.id, candidate);
        }

        private static bool TryResolveImage(
            string directory,
            string configuredPath,
            out string fullPath,
            out string reason)
        {
            fullPath = null;
            reason = null;

            try
            {
                string relative = configuredPath.Trim().Replace('/', Path.DirectorySeparatorChar);
                if (Path.IsPathRooted(relative))
                {
                    reason = "image 必须是相对于当前 ScreenPaper 文件夹的路径";
                    return false;
                }

                string root = Path.GetFullPath(directory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string candidate = Path.GetFullPath(Path.Combine(root, relative));
                string prefix = root + Path.DirectorySeparatorChar;
                if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    reason = "image 不得使用 .. 跳出当前 ScreenPaper 文件夹";
                    return false;
                }

                string extension = Path.GetExtension(candidate);
                if (!SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    reason = "只支持 PNG、JPG、JPEG";
                    return false;
                }

                if (!File.Exists(candidate))
                {
                    reason = $"文件不存在：{candidate}";
                    return false;
                }

                fullPath = candidate;
                return true;
            }
            catch (Exception exception)
            {
                reason = exception.Message;
                return false;
            }
        }

        private static IEnumerable<string> EnumerateConfigFiles(IReadOnlyList<ContentRoot> roots)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ContentRoot root in roots ?? Array.Empty<ContentRoot>())
            {
                string[] directories =
                {
                    Path.Combine(root.Path, "EC2BUnofficialPatch", "ScreenPaper"),
                    Path.Combine(root.Path, "ScreenPaper")
                };

                foreach (string directory in directories)
                {
                    string file = Path.Combine(directory, "Custompaper.json");
                    if (File.Exists(file))
                    {
                        string fullPath = Path.GetFullPath(file);
                        if (seen.Add(fullPath))
                        {
                            yield return fullPath;
                        }
                    }
                }
            }
        }
    }
}
