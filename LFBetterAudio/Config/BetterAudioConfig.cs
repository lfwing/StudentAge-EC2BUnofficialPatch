using System.Collections.Generic;
using Newtonsoft.Json;

namespace LFBetterAudio.Config
{
    public sealed class BetterAudioConfigRoot
    {
        [JsonProperty("audios")]
        public List<BetterAudioEntry> Audios { get; set; } = new List<BetterAudioEntry>();

        // 仅用于平滑迁移旧资源包；新模板统一使用 audios。
        [JsonProperty("musics")]
        public List<BetterAudioEntry> LegacyMusics { get; set; } = new List<BetterAudioEntry>();

        public IEnumerable<BetterAudioEntry> EnumerateEntries()
        {
            foreach (BetterAudioEntry entry in Audios ?? new List<BetterAudioEntry>())
            {
                yield return entry;
            }

            foreach (BetterAudioEntry entry in LegacyMusics ?? new List<BetterAudioEntry>())
            {
                yield return entry;
            }
        }
    }

    public sealed class BetterAudioEntry
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("audioPath")]
        public string AudioPath { get; set; }

        [JsonProperty("timelinePath")]
        public string TimelinePath { get; set; }

        [JsonProperty("volume")]
        public float Volume { get; set; } = 1f;

        /// <summary>
        /// 与原版 AudioCfg 保持一致：1=音乐，2=音效。
        /// 缺省值或非法值统一按 1（音乐）处理。
        /// </summary>
        [JsonProperty("type")]
        public int Type { get; set; } = 1;

        // 以下字段只在运行时记录“这个条目来自哪个 Mod”，不会写回 JSON。
        // 相对路径必须相对该条目自己的 BetterAudio 目录解析，不能统一相对 BepInEx。
        [JsonIgnore]
        public string SourceDirectory { get; set; }

        [JsonIgnore]
        public string SourceConfigPath { get; set; }

        [JsonIgnore]
        public string SourceLabel { get; set; }
    }
}
