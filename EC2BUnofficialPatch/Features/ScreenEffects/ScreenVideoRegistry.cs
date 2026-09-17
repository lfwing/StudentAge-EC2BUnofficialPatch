using System;
using System.Collections.Generic;
using System.IO;
using EC2BUnofficialPatch.Core;
using EC2BUnofficialPatch.Workshop;
using Newtonsoft.Json;

namespace EC2BUnofficialPatch.Features.ScreenEffects
{
    internal sealed class ScreenVideoDatabase
    {
        public List<ScreenVideoEntry> videos { get; set; } = new List<ScreenVideoEntry>();
    }

    internal sealed class ScreenVideoEntry
    {
        public int id { get; set; }
        public string name { get; set; }
        public string video { get; set; }
        public float volume { get; set; } = 1f;
        public bool loop { get; set; }
        public bool skippable { get; set; } = true;
        public bool block { get; set; } = true;
        public bool roleOnTop { get; set; }
        public string scale { get; set; } = "fit";
    }

    internal sealed class RegisteredScreenVideo
    {
        internal RegisteredScreenVideo(int id, string name, string videoPath, float volume, bool loop, bool skippable, bool block, bool roleOnTop, string scale, string sourcePath)
        {
            Id = id;
            Name = name ?? string.Empty;
            VideoPath = videoPath;
            Volume = volume;
            Loop = loop;
            Skippable = skippable;
            Block = block;
            RoleOnTop = roleOnTop;
            Scale = scale;
            SourcePath = sourcePath;
        }

        internal int Id { get; }
        internal string Name { get; }
        internal string VideoPath { get; }
        internal float Volume { get; }
        internal bool Loop { get; }
        internal bool Skippable { get; }
        internal bool Block { get; }
        internal bool RoleOnTop { get; }
        internal string Scale { get; }
        internal string SourcePath { get; }
    }

    internal sealed class ScreenVideoRegistry
    {
        internal const string FolderName = "ScreenVideo";
        internal const string FileName = "CustomVideo.json";
        private static readonly string[] SupportedExtensions = { ".mp4", ".webm", ".mov", ".m4v" };

        private readonly Dictionary<int, RegisteredScreenVideo> _videos = new Dictionary<int, RegisteredScreenVideo>();
        private readonly HashSet<string> _reportedMissing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal int Count => _videos.Count;

        internal static ScreenVideoRegistry Load(IReadOnlyList<ContentRoot> roots)
        {
            var registry = new ScreenVideoRegistry();
            foreach (string configPath in EnumerateConfigFiles(roots))
            {
                registry.LoadFile(configPath);
            }
            return registry;
        }

        internal bool TryGet(int id, out RegisteredScreenVideo video)
        {
            return _videos.TryGetValue(id, out video);
        }

        internal void ReportMissingFile(RegisteredScreenVideo video)
        {
            if (video == null || !_reportedMissing.Add(video.VideoPath)) return;
            PatchLog.Warning($"1164屏幕视频扩展-id={video.Id} 的视频文件不存在，已忽略：{video.VideoPath}, source={video.SourcePath}");
        }

        private void LoadFile(string configPath)
        {
            ScreenVideoDatabase database;
            try
            {
                database = JsonConvert.DeserializeObject<ScreenVideoDatabase>(File.ReadAllText(configPath));
            }
            catch (Exception exception)
            {
                PatchLog.Warning($"1164屏幕视频扩展-无法读取 {configPath}：{ModuleHost.GetReason(exception)}");
                return;
            }

            if (database?.videos == null) return;
            string directory = Path.GetDirectoryName(configPath) ?? string.Empty;
            string directoryFull = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            foreach (ScreenVideoEntry entry in database.videos)
            {
                if (entry == null) continue;
                if (entry.id <= 0)
                {
                    PatchLog.Warning($"1164屏幕视频扩展-id 必须是正整数，已忽略：id={entry.id}, source={configPath}");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(entry.video) || Path.IsPathRooted(entry.video) || entry.video.Contains(".."))
                {
                    PatchLog.Warning($"1164屏幕视频扩展-id={entry.id} 的 video 必须是本目录内的相对路径，已忽略：source={configPath}");
                    continue;
                }
                string extension = Path.GetExtension(entry.video);
                if (Array.IndexOf(SupportedExtensions, extension.ToLowerInvariant()) < 0)
                {
                    PatchLog.Warning($"1164屏幕视频扩展-id={entry.id} 的视频格式不支持（{extension}），仅支持 mp4/webm/mov/m4v：source={configPath}");
                    continue;
                }
                string fullPath = Path.GetFullPath(Path.Combine(directory, entry.video));
                if (!fullPath.StartsWith(directoryFull, StringComparison.OrdinalIgnoreCase))
                {
                    PatchLog.Warning($"1164屏幕视频扩展-id={entry.id} 的 video 跳出了 ScreenVideo 目录，已忽略：source={configPath}");
                    continue;
                }
                if (_videos.TryGetValue(entry.id, out RegisteredScreenVideo existing))
                {
                    PatchLog.Warning($"1164屏幕视频扩展-id={entry.id} 重复注册，保留先加载的 {existing.SourcePath}，忽略 {configPath}");
                    continue;
                }

                string scale = (entry.scale ?? "fit").Trim().ToLowerInvariant();
                if (scale != "fit" && scale != "fill" && scale != "stretch") scale = "fit";
                float volume = float.IsNaN(entry.volume) ? 1f : Math.Max(0f, Math.Min(1f, entry.volume));
                _videos[entry.id] = new RegisteredScreenVideo(entry.id, entry.name, fullPath, volume, entry.loop, entry.skippable, entry.block, entry.roleOnTop, scale, configPath);
            }
        }

        private static IEnumerable<string> EnumerateConfigFiles(IReadOnlyList<ContentRoot> roots)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ContentRoot root in roots ?? Array.Empty<ContentRoot>())
            {
                string[] directories =
                {
                    Path.Combine(root.Path, "EC2BUnofficialPatch", FolderName),
                    Path.Combine(root.Path, FolderName)
                };
                foreach (string directory in directories)
                {
                    string file = Path.Combine(directory, FileName);
                    if (!File.Exists(file)) continue;
                    string fullPath = Path.GetFullPath(file);
                    if (seen.Add(fullPath)) yield return fullPath;
                }
            }
        }
    }
}
