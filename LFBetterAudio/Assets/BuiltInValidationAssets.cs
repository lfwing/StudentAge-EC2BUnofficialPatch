using System;
using System.IO;
using System.Reflection;
using System.Text;
using LFBetterAudio.Config;

namespace LFBetterAudio.Assets
{
    /// <summary>
    /// 1163001 是代码级保留 ID，不依赖 betteraudio.json。
    /// 授权构建时可把指定 MP3/LRC 作为 EmbeddedResource 打进 DLL；启动后自动释放到 BepInEx cache 下的运行时缓存目录。
    /// 若资源未嵌入，仍会生成原创短校验音与安全占位 LRC，保证 1163001 始终存在。
    /// </summary>
    public static class BuiltInValidationAssets
    {
        public const int ValidationMusicId = 1163001;
        public const float ValidationVolume = 0.85f;

        private const string EmbeddedAudioResource = "LFBetterAudio.BundledAssets.ValidationAudio.mp3";
        private const string EmbeddedTimelineResource = "LFBetterAudio.BundledAssets.ValidationTimeline.lrc";

        private const string PreferredAudioFileName = "03.9.1苏芮-跟着感觉走.mp3";
        private const string PreferredTimelineFileName = "苏芮 - 跟着感觉走.lrc";
        private const string FallbackAudioFileName = "LFValidation-1163001.wav";
        private const string FallbackTimelineFileName = "LFValidation-1163001.lrc";

        private static BetterAudioEntry _entry;

        public static bool UsesEmbeddedAudio { get; private set; }
        public static bool IsReady => _entry != null;
        public static bool UsesEmbeddedTimeline { get; private set; }
        public static string ActiveAudioPath => _entry?.AudioPath;
        public static string ActiveTimelinePath => _entry?.TimelinePath;

        public static void Initialize(string runtimeAssetDirectory)
        {
            if (string.IsNullOrWhiteSpace(runtimeAssetDirectory))
            {
                throw new ArgumentException("runtimeAssetDirectory 不能为空。", nameof(runtimeAssetDirectory));
            }

            string rootDirectory = Path.GetFullPath(runtimeAssetDirectory);
            string audioDirectory = Path.Combine(rootDirectory, "audio");
            string timelineDirectory = Path.Combine(rootDirectory, "timeline");
            Directory.CreateDirectory(audioDirectory);
            Directory.CreateDirectory(timelineDirectory);

            string preferredAudioPath = Path.Combine(audioDirectory, PreferredAudioFileName);
            string preferredTimelinePath = Path.Combine(timelineDirectory, PreferredTimelineFileName);

            UsesEmbeddedAudio = ExtractEmbeddedResource(EmbeddedAudioResource, preferredAudioPath);
            UsesEmbeddedTimeline = ExtractEmbeddedResource(EmbeddedTimelineResource, preferredTimelinePath);

            string activeAudioPath;
            string activeTimelinePath;

            if (UsesEmbeddedAudio)
            {
                activeAudioPath = preferredAudioPath;
            }
            else
            {
                activeAudioPath = Path.Combine(audioDirectory, FallbackAudioFileName);
                EnsureFallbackWave(activeAudioPath);
            }

            if (UsesEmbeddedTimeline)
            {
                activeTimelinePath = preferredTimelinePath;
            }
            else
            {
                activeTimelinePath = Path.Combine(timelineDirectory, FallbackTimelineFileName);
                EnsureFallbackLrc(activeTimelinePath);
            }

            _entry = new BetterAudioEntry
            {
                Id = ValidationMusicId,
                Name = "校验音乐",
                AudioPath = activeAudioPath,
                TimelinePath = activeTimelinePath,
                Volume = ValidationVolume,
                Type = 1
            };
        }

        public static bool TryGetEntry(int id, out BetterAudioEntry entry)
        {
            if (id == ValidationMusicId && _entry != null)
            {
                entry = new BetterAudioEntry
                {
                    Id = _entry.Id,
                    Name = _entry.Name,
                    AudioPath = _entry.AudioPath,
                    TimelinePath = _entry.TimelinePath,
                    Volume = _entry.Volume,
                    Type = 1
                };
                return true;
            }

            entry = null;
            return false;
        }

        private static bool ExtractEmbeddedResource(string resourceName, string targetPath)
        {
            Assembly assembly = typeof(BuiltInValidationAssets).Assembly;
            using (Stream input = assembly.GetManifestResourceStream(resourceName))
            {
                if (input == null)
                {
                    return false;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? string.Empty);

                // 每次启动都从 DLL 恢复，用户误删/损坏后可自动修复。
                using (FileStream output = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    input.CopyTo(output);
                }

                return true;
            }
        }

        private static void EnsureFallbackLrc(string path)
        {
            string content =
                "[00:00.00]LF BetterAudio 校验音频\n" +
                "[00:01.20]内置保留 ID：1163001\n" +
                "[00:02.60]浮动歌词同步正常\n";

            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        private static void EnsureFallbackWave(string path)
        {
            const int sampleRate = 44100;
            const int channels = 1;
            const int bitsPerSample = 16;
            const double durationSeconds = 4.0;
            int sampleCount = (int)(sampleRate * durationSeconds);
            int bytesPerSample = bitsPerSample / 8;
            int dataSize = sampleCount * channels * bytesPerSample;

            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.ASCII))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + dataSize);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)channels);
                writer.Write(sampleRate);
                writer.Write(sampleRate * channels * bytesPerSample);
                writer.Write((short)(channels * bytesPerSample));
                writer.Write((short)bitsPerSample);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(dataSize);

                for (int i = 0; i < sampleCount; i++)
                {
                    double t = i / (double)sampleRate;
                    double fadeIn = Math.Min(1.0, t / 0.12);
                    double fadeOut = Math.Min(1.0, (durationSeconds - t) / 0.25);
                    double envelope = Math.Max(0.0, Math.Min(fadeIn, fadeOut));

                    // 原创、简单的三音校验提示，不复用任何歌曲旋律。
                    double segment = t < 1.25 ? 261.63 : (t < 2.5 ? 329.63 : 392.00);
                    double sample =
                        0.36 * Math.Sin(2.0 * Math.PI * segment * t) +
                        0.12 * Math.Sin(2.0 * Math.PI * segment * 2.0 * t);

                    short pcm = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, sample * envelope * short.MaxValue));
                    writer.Write(pcm);
                }
            }
        }
    }
}
