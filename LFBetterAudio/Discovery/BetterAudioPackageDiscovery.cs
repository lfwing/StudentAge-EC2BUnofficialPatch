using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace LFBetterAudio.Discovery
{
    /// <summary>
    /// 发现已安装的 StudentAge Steam 创意工坊 Mod 中的 BetterAudio 音频资源包。
    /// 不复制文件；只登记每个包自己的物理目录。
    /// </summary>
    public static class BetterAudioPackageDiscovery
    {
        public const string DefaultWorkshopAppId = "1991040";
        public const string BetterAudioFolderName = "BetterAudio";
        public const string BetterAudioConfigFileName = "BetterAudio.json";

        private static readonly Regex VdfPathRegex = new Regex(
            "\\\"path\\\"\\s*\\\"(?<path>[^\\\"]+)\\\"",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // 兼容较旧 libraryfolders.vdf："1" "D:\\SteamLibrary"
        private static readonly Regex LegacyVdfLibraryRegex = new Regex(
            "\\\"\\d+\\\"\\s*\\\"(?<path>[A-Za-z]:\\\\[^\\\"]+)\\\"",
            RegexOptions.Compiled);

        public static IReadOnlyList<BetterAudioPackage> DiscoverWorkshopPackages(string gameRootPath)
        {
            string appId = ResolveAppId(gameRootPath);
            var packages = new List<BetterAudioPackage>();
            var seenConfigPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string libraryRoot in DiscoverSteamLibraryRoots(gameRootPath))
            {
                string workshopRoot = Path.Combine(
                    libraryRoot,
                    "steamapps",
                    "workshop",
                    "content",
                    appId);

                if (!Directory.Exists(workshopRoot))
                {
                    continue;
                }

                IEnumerable<string> itemRoots;
                try
                {
                    itemRoots = Directory.EnumerateDirectories(workshopRoot)
                        .OrderBy(GetWorkshopSortKey, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                }
                catch (Exception ex)
                {
                    Plugin.ReportStartupIssue(
                        $"无法枚举创意工坊目录 {workshopRoot}：{ex.Message}");
                    continue;
                }

                foreach (string itemRoot in itemRoots)
                {
                    string betterAudioDirectory = FindDirectChildDirectory(
                        itemRoot,
                        BetterAudioFolderName);
                    if (betterAudioDirectory == null)
                    {
                        continue;
                    }

                    string configPath = FindDirectChildFile(
                        betterAudioDirectory,
                        BetterAudioConfigFileName);
                    if (configPath == null)
                    {
                        // 用户已说明 Mod 作者会按模板创建；这里静默跳过非 BetterAudio 包。
                        continue;
                    }

                    string fullConfigPath = SafeFullPath(configPath);
                    if (!seenConfigPaths.Add(fullConfigPath))
                    {
                        continue;
                    }

                    string workshopItemId = new DirectoryInfo(itemRoot).Name;
                    packages.Add(new BetterAudioPackage
                    {
                        ModRootDirectory = SafeFullPath(itemRoot),
                        BetterAudioDirectory = SafeFullPath(betterAudioDirectory),
                        ConfigPath = fullConfigPath,
                        SourceLabel = $"Workshop:{workshopItemId}",
                        WorkshopItemId = workshopItemId,
                        IsWorkshopPackage = true
                    });
                }
            }

            return packages
                .OrderBy(p => GetNumericWorkshopId(p.WorkshopItemId))
                .ThenBy(p => p.WorkshopItemId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.ConfigPath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static IReadOnlyList<string> DiscoverSteamLibraryRoots(string gameRootPath)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddLibraryRoot(string candidate)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    return;
                }

                try
                {
                    string full = Path.GetFullPath(candidate.Trim());
                    if (Directory.Exists(full) && seen.Add(full))
                    {
                        result.Add(full);
                    }
                }
                catch
                {
                    // 单个候选路径非法不应中断整个扫描。
                }
            }

            // 最可靠：由当前游戏安装目录反推当前 Steam Library 根。
            AddLibraryRoot(TryDeriveLibraryRootFromGameRoot(gameRootPath));

            // 常见 Steam 安装位置。
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(programFilesX86))
            {
                AddLibraryRoot(Path.Combine(programFilesX86, "Steam"));
            }
            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                AddLibraryRoot(Path.Combine(programFiles, "Steam"));
            }

            // Windows Steam 注册表。
            AddLibraryRoot(ReadSteamRootFromRegistry(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"));
            AddLibraryRoot(ReadSteamRootFromRegistry(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"));
            AddLibraryRoot(ReadSteamRootFromRegistry(Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"));

            // 每发现一个 libraryfolders.vdf，就把其中所有库加入集合；循环直到没有新增。
            for (int i = 0; i < result.Count; i++)
            {
                string vdfPath = Path.Combine(result[i], "steamapps", "libraryfolders.vdf");
                foreach (string parsed in ParseLibraryFoldersVdf(vdfPath))
                {
                    AddLibraryRoot(parsed);
                }
            }

            return result.ToArray();
        }

        private static string ResolveAppId(string gameRootPath)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(gameRootPath))
                {
                    string appIdFile = Path.Combine(gameRootPath, "steam_appid.txt");
                    if (File.Exists(appIdFile))
                    {
                        string value = File.ReadAllText(appIdFile).Trim();
                        if (value.Length > 0 && value.All(char.IsDigit))
                        {
                            return value;
                        }
                    }
                }
            }
            catch
            {
                // 回退到本游戏固定 App ID。
            }

            return DefaultWorkshopAppId;
        }

        private static string TryDeriveLibraryRootFromGameRoot(string gameRootPath)
        {
            if (string.IsNullOrWhiteSpace(gameRootPath))
            {
                return null;
            }

            try
            {
                DirectoryInfo gameDir = new DirectoryInfo(Path.GetFullPath(gameRootPath));
                DirectoryInfo commonDir = gameDir.Parent;
                DirectoryInfo steamAppsDir = commonDir?.Parent;
                DirectoryInfo libraryRoot = steamAppsDir?.Parent;

                if (commonDir != null &&
                    steamAppsDir != null &&
                    libraryRoot != null &&
                    commonDir.Name.Equals("common", StringComparison.OrdinalIgnoreCase) &&
                    steamAppsDir.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase))
                {
                    return libraryRoot.FullName;
                }
            }
            catch
            {
                // 返回 null，由其他发现路径接管。
            }

            return null;
        }

        private static IEnumerable<string> ParseLibraryFoldersVdf(string vdfPath)
        {
            if (string.IsNullOrWhiteSpace(vdfPath) || !File.Exists(vdfPath))
            {
                yield break;
            }

            string text;
            try
            {
                text = File.ReadAllText(vdfPath);
            }
            catch
            {
                yield break;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match match in VdfPathRegex.Matches(text))
            {
                string path = DecodeVdfPath(match.Groups["path"].Value);
                if (!string.IsNullOrWhiteSpace(path) && seen.Add(path))
                {
                    yield return path;
                }
            }

            foreach (Match match in LegacyVdfLibraryRegex.Matches(text))
            {
                string path = DecodeVdfPath(match.Groups["path"].Value);
                if (!string.IsNullOrWhiteSpace(path) && seen.Add(path))
                {
                    yield return path;
                }
            }
        }

        private static string DecodeVdfPath(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value.Replace("\\\\", "\\").Trim();
        }

        private static string ReadSteamRootFromRegistry(RegistryKey hive, string subKeyPath, string valueName)
        {
            try
            {
                using (RegistryKey key = hive.OpenSubKey(subKeyPath))
                {
                    return key?.GetValue(valueName) as string;
                }
            }
            catch
            {
                return null;
            }
        }

        private static string FindDirectChildDirectory(string parentDirectory, string expectedName)
        {
            try
            {
                return Directory.EnumerateDirectories(parentDirectory)
                    .FirstOrDefault(path =>
                        string.Equals(
                            new DirectoryInfo(path).Name,
                            expectedName,
                            StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }

        private static string FindDirectChildFile(string parentDirectory, string expectedName)
        {
            try
            {
                return Directory.EnumerateFiles(parentDirectory)
                    .FirstOrDefault(path =>
                        string.Equals(
                            Path.GetFileName(path),
                            expectedName,
                            StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }

        private static string SafeFullPath(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return path;
            }
        }

        private static string GetWorkshopSortKey(string path)
        {
            string name = new DirectoryInfo(path).Name;
            return GetNumericWorkshopId(name).ToString("D20") + ":" + name;
        }

        private static ulong GetNumericWorkshopId(string value)
        {
            return ulong.TryParse(value, out ulong parsed) ? parsed : ulong.MaxValue;
        }
    }
}
