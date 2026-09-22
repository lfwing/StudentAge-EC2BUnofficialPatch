using System;
using Config;
using EC2BUnofficialPatch.Core;
using LFBetterAudio.Audio;
using LFBetterAudio.Runtime;

namespace EC2BUnofficialPatch.Features.ScreenEffects.ScreenLyrcis
{
    internal sealed class PerformanceAudioPlan
    {
        internal PerformanceAudioPlan(int id, string path, float volume)
        {
            Id = id;
            Path = path;
            Volume = volume;
        }

        internal int Id { get; }
        internal string Path { get; }
        internal float Volume { get; }

        internal static bool TryResolve(int id, bool preview, out PerformanceAudioPlan plan, out string error)
        {
            plan = null;
            error = null;

            // Original AudioCfg IDs keep their existing meaning when BA is enabled.
            if (Cfg.AudioCfgMap != null && Cfg.AudioCfgMap.TryGetValue(id, out AudioCfg original) &&
                original != null && !string.IsNullOrWhiteSpace(original.url))
            {
                if (original.type == 2)
                {
                    error = $"audio={id} 在原版 AudioCfg 中是音效(type=2)，5002 只接受音乐(type=1)。";
                    return false;
                }

                string originalPath = AudioMgrEx.FormatUrl(original.url);
                if (string.IsNullOrWhiteSpace(originalPath))
                {
                    error = $"audio={id} 的原版音频路径为空。";
                    return false;
                }

                plan = new PerformanceAudioPlan(id, originalPath,
                    original.volumn > 0f ? Math.Min(original.volumn, 1f) : 1f);
                return true;
            }

            if (PluginConfig.BetterAudio?.Value != true || LFBetterAudio.Plugin.Instance == null)
            {
                error = $"audio={id} 未在当前原版 AudioCfg 中注册，且《更好的音频效果》未启用。";
                return false;
            }

            try
            {
                var context = new AudioResolveContext
                {
                    Channel = preview ? TalkChannel.Preview : TalkChannel.Runtime
                };
                if (!AudioResolver.TryResolve(id, context, out ResolvedAudio resolved, out error))
                    return false;

                if (resolved.AudioType == BetterAudioType.SoundEffect)
                {
                    error = $"audio={id} 在 BetterAudio 中注册为音效(type=2)，5002 只接受音乐(type=1)。";
                    return false;
                }

                plan = new PerformanceAudioPlan(id, resolved.AudioPath, resolved.Volume);
                return true;
            }
            catch (Exception exception)
            {
                error = "BetterAudio 音频解析失败：" + ModuleHost.GetReason(exception);
                return false;
            }
        }
    }

    internal sealed class BetterAudioPauseLease
    {
        internal BetterAudioController Controller;
        internal BetterAudioSession Session;
        internal long SessionToken;
    }

    internal static class BetterAudioBridge
    {
        internal static BetterAudioPauseLease PauseActiveMusic()
        {
            if (PluginConfig.BetterAudio?.Value != true) return null;
            try
            {
                BetterAudioController controller = BetterAudioController.Instance;
                BetterAudioSession session = controller?.ActiveSession;
                if (session == null || session.IsCancelled || session.IsPaused ||
                    session.PendingPauseAfterLoad || (!session.IsLoading && !session.IsPlaying))
                    return null;

                controller.PauseActiveMusic(false);
                if (!session.IsPaused && !session.PendingPauseAfterLoad) return null;
                return new BetterAudioPauseLease
                {
                    Controller = controller,
                    Session = session,
                    SessionToken = session.Token
                };
            }
            catch (Exception exception)
            {
                PatchLog.Warning("5002屏幕滚动歌词扩展-自动暂停 BetterAudio 失败：" +
                    ModuleHost.GetReason(exception));
                return null;
            }
        }

        internal static void Resume(BetterAudioPauseLease lease)
        {
            if (lease == null) return;
            try
            {
                if (BetterAudioController.Instance != lease.Controller ||
                    lease.Controller.ActiveSession != lease.Session ||
                    lease.Session.Token != lease.SessionToken)
                    return;
                lease.Controller.ResumeActiveMusic(false);
            }
            catch (Exception exception)
            {
                PatchLog.Warning("5002屏幕滚动歌词扩展-恢复 BetterAudio 失败：" +
                    ModuleHost.GetReason(exception));
            }
        }
    }
}
