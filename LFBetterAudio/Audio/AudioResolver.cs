using System.Collections.Generic;
using System.IO;
using Config;
using LFBetterAudio.Assets;
using LFBetterAudio.Config;
using LFBetterAudio.Runtime;
using UnityEngine;

namespace LFBetterAudio.Audio
{
    public static class AudioResolver
    {
        public static bool TryResolve(
            int musicId,
            AudioResolveContext context,
            out ResolvedAudio resolved,
            out string error)
        {
            resolved = null;
            error = null;

            // 1163001 永远优先于 JSON 与原版 AudioCfg，属于代码级保留音乐。
            if (BuiltInValidationAssets.TryGetEntry(musicId, out BetterAudioEntry builtIn))
            {
                if (string.IsNullOrWhiteSpace(builtIn.AudioPath) || !File.Exists(builtIn.AudioPath))
                {
                    error = $"内置校验音乐 ID={musicId} 的释放文件不存在：{builtIn.AudioPath}";
                    return false;
                }

                string builtInLrc = !string.IsNullOrWhiteSpace(builtIn.TimelinePath) && File.Exists(builtIn.TimelinePath)
                    ? builtIn.TimelinePath
                    : null;

                resolved = new ResolvedAudio
                {
                    Id = musicId,
                    Name = builtIn.Name,
                    AudioPath = builtIn.AudioPath,
                    TimelinePath = builtInLrc,
                    Volume = NormalizeVolume(builtIn.Volume, true),
                    AudioType = BetterAudioType.Music,
                    UsesExternalMusicFile = true,
                    UsesOriginalGameAudio = false
                };
                return true;
            }

            BetterAudioEntry entry = null;
            Plugin.ConfigStore?.TryGet(musicId, out entry);

            Dictionary<int, AudioCfg> primaryMap = context?.Channel == TalkChannel.Preview
                ? context.PreviewAudioCfgMap
                : null;

            AudioCfg originalCfg = null;
            if (primaryMap != null)
            {
                primaryMap.TryGetValue(musicId, out originalCfg);
            }
            if (originalCfg == null)
            {
                Cfg.AudioCfgMap?.TryGetValue(musicId, out originalCfg);
            }

            string audioPath = null;
            bool external = false;
            bool original = false;

            if (entry != null && !string.IsNullOrWhiteSpace(entry.AudioPath))
            {
                audioPath = BetterAudioConfigStore.ResolvePath(entry, entry.AudioPath);
                external = true;
                if (!File.Exists(audioPath))
                {
                    error = $"音频 ID={musicId} 的自定义文件不存在：{audioPath}；来源={entry.SourceLabel}";
                    return false;
                }
            }
            else if (originalCfg != null && !string.IsNullOrWhiteSpace(originalCfg.url))
            {
                // JSON audioPath 为空时，允许给原版音频补外挂 LRC/音量/类型。
                audioPath = AudioMgrEx.FormatUrl(originalCfg.url);
                original = true;
            }

            if (string.IsNullOrWhiteSpace(audioPath))
            {
                error = $"音频 ID={musicId} 既没有可用的 JSON audioPath，也不在当前 AudioCfgMap/Cfg.AudioCfgMap 中。";
                return false;
            }

            string timelinePath = null;
            if (entry != null && !string.IsNullOrWhiteSpace(entry.TimelinePath))
            {
                timelinePath = BetterAudioConfigStore.ResolvePath(entry, entry.TimelinePath);
                if (!File.Exists(timelinePath))
                {
                    timelinePath = null;
                }
            }

            float volume;
            if (entry != null)
            {
                volume = NormalizeVolume(entry.Volume, true);
            }
            else if (originalCfg != null)
            {
                // 原版 AudioMgrEx 将 volumn<=0 视作 1，而不是静音。
                volume = NormalizeVolume(originalCfg.volumn, false);
            }
            else
            {
                volume = 1f;
            }

            BetterAudioType audioType = entry != null
                ? NormalizeAudioType(entry.Type)
                : NormalizeAudioType(originalCfg?.type ?? 1);

            resolved = new ResolvedAudio
            {
                Id = musicId,
                Name = !string.IsNullOrWhiteSpace(entry?.Name)
                    ? entry.Name
                    : (!string.IsNullOrWhiteSpace(originalCfg?.name) ? originalCfg.name : $"Audio {musicId}"),
                AudioPath = audioPath,
                TimelinePath = timelinePath,
                Volume = volume,
                AudioType = audioType,
                UsesExternalMusicFile = external,
                UsesOriginalGameAudio = original
            };
            return true;
        }

        public static BetterAudioType NormalizeAudioType(int rawType)
        {
            return rawType == (int)BetterAudioType.SoundEffect
                ? BetterAudioType.SoundEffect
                : BetterAudioType.Music;
        }

        private static float NormalizeVolume(float rawVolume, bool explicitJsonVolume)
        {
            if (!explicitJsonVolume && rawVolume <= 0f)
            {
                return 1f;
            }

            return Mathf.Clamp01(rawVolume);
        }
    }
}
