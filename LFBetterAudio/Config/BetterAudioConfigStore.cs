using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LFBetterAudio.Assets;
using LFBetterAudio.Discovery;
using Newtonsoft.Json;

namespace LFBetterAudio.Config
{
    public sealed class BetterAudioConfigStore
    {
        private readonly Dictionary<int, BetterAudioEntry> _entries;

        private BetterAudioConfigStore(
            Dictionary<int, BetterAudioEntry> entries,
            int packageCount,
            int workshopPackageCount)
        {
            _entries = entries ?? new Dictionary<int, BetterAudioEntry>();
            PackageCount = packageCount;
            WorkshopPackageCount = workshopPackageCount;
        }

        /// <summary>仅统计 Mod JSON 中实际可用的条目；不含代码级保留 ID 1163001。</summary>
        public int Count => _entries.Count;

        /// <summary>本次实际读取成功的 BetterAudio JSON 资源包数量。</summary>
        public int PackageCount { get; }

        /// <summary>本次实际读取成功的创意工坊 BetterAudio 音频资源包数量。</summary>
        public int WorkshopPackageCount { get; }

        public bool TryGet(int id, out BetterAudioEntry entry)
        {
            return _entries.TryGetValue(id, out entry);
        }

        public static BetterAudioConfigStore CreateEmpty()
        {
            return new BetterAudioConfigStore(
                new Dictionary<int, BetterAudioEntry>(),
                0,
                0);
        }

        /// <summary>
        /// 读取所有发现到的 Mod/创意工坊 BetterAudio 音频资源包。
        /// BepInEx/plugins/BetterAudio 只是作者模板目录，不参与资源注册。
        /// 每个条目的相对路径始终绑定到自己的来源 BetterAudio 目录。
        /// </summary>
        public static BetterAudioConfigStore LoadAll(
            IEnumerable<BetterAudioPackage> discoveredPackages)
        {
            var entries = new Dictionary<int, BetterAudioEntry>();
            int packageCount = 0;
            int workshopPackageCount = 0;

            foreach (BetterAudioPackage package in (discoveredPackages ?? Enumerable.Empty<BetterAudioPackage>())
                         .Where(p => p != null)
                         .OrderBy(p => p.IsWorkshopPackage ? 0 : 1)
                         .ThenBy(p => ParseWorkshopId(p.WorkshopItemId))
                         .ThenBy(p => p.SourceLabel ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(p => p.ConfigPath ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                if (!TryMergePackage(package, entries))
                {
                    continue;
                }

                packageCount++;
                if (package.IsWorkshopPackage)
                {
                    workshopPackageCount++;
                }
            }

            return new BetterAudioConfigStore(
                entries,
                packageCount,
                workshopPackageCount);
        }

        public static string ResolvePath(BetterAudioEntry entry, string relativeOrAbsolutePath)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            string sourceDirectory = entry.SourceDirectory;
            if (string.IsNullOrWhiteSpace(sourceDirectory) &&
                !string.IsNullOrWhiteSpace(entry.SourceConfigPath))
            {
                sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(entry.SourceConfigPath));
            }

            if (string.IsNullOrWhiteSpace(sourceDirectory))
            {
                throw new InvalidOperationException(
                    $"音频条目 ID={entry.Id} 缺少来源目录，无法解析相对路径。来源={entry.SourceLabel ?? "<unknown>"}");
            }

            return ResolvePath(sourceDirectory, relativeOrAbsolutePath);
        }

        public static string ResolvePath(string baseDirectory, string relativeOrAbsolutePath)
        {
            if (string.IsNullOrWhiteSpace(relativeOrAbsolutePath))
            {
                return null;
            }

            string path = relativeOrAbsolutePath.Trim();
            if (Path.IsPathRooted(path))
            {
                return Path.GetFullPath(path);
            }

            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                throw new ArgumentException("解析相对路径时 baseDirectory 不能为空。", nameof(baseDirectory));
            }

            return Path.GetFullPath(Path.Combine(baseDirectory, path));
        }

        private static bool TryMergePackage(
            BetterAudioPackage package,
            IDictionary<int, BetterAudioEntry> entries)
        {
            if (package == null ||
                string.IsNullOrWhiteSpace(package.ConfigPath) ||
                !File.Exists(package.ConfigPath))
            {
                return false;
            }

            BetterAudioConfigRoot root;
            try
            {
                string json = File.ReadAllText(package.ConfigPath, Encoding.UTF8);
                root = JsonConvert.DeserializeObject<BetterAudioConfigRoot>(json)
                       ?? new BetterAudioConfigRoot();
            }
            catch (Exception ex)
            {
                // 单个公开 Mod 配置损坏不能拖垮其他 Mod 与整个插件。
                Plugin.ReportStartupIssue(
                    $"资源包 {package.SourceLabel} 的 JSON 解析失败：{ex.Message}");
                return false;
            }

            string sourceDirectory = !string.IsNullOrWhiteSpace(package.BetterAudioDirectory)
                ? Path.GetFullPath(package.BetterAudioDirectory)
                : Path.GetDirectoryName(Path.GetFullPath(package.ConfigPath));

            foreach (BetterAudioEntry rawEntry in root.EnumerateEntries())
            {
                if (rawEntry == null || rawEntry.Id <= 0)
                {
                    Plugin.ReportStartupIssue(
                        $"资源包 {package.SourceLabel} 存在音频 ID 不大于 0 的无效条目。");
                    continue;
                }

                // 1163001 永远是代码级保留 ID，公开 Mod 不得覆盖。
                if (rawEntry.Id == BuiltInValidationAssets.ValidationMusicId)
                {
                    continue;
                }

                var entry = new BetterAudioEntry
                {
                    Id = rawEntry.Id,
                    Name = rawEntry.Name,
                    AudioPath = rawEntry.AudioPath,
                    TimelinePath = rawEntry.TimelinePath,
                    Volume = rawEntry.Volume,
                    Type = rawEntry.Type == 2 ? 2 : 1,
                    SourceDirectory = sourceDirectory,
                    SourceConfigPath = Path.GetFullPath(package.ConfigPath),
                    SourceLabel = package.SourceLabel
                };

                if (entries.TryGetValue(entry.Id, out BetterAudioEntry existing))
                {
                    Plugin.ReportStartupIssue(
                        $"音频 ID={entry.Id} 冲突：已由 {existing.SourceLabel} 注册，" +
                        $"后加载来源 {entry.SourceLabel} 被忽略。");
                    continue;
                }

                entries[entry.Id] = entry;
            }

            return true;
        }

        private static ulong ParseWorkshopId(string value)
        {
            return ulong.TryParse(value, out ulong parsed) ? parsed : ulong.MaxValue;
        }
    }
}
