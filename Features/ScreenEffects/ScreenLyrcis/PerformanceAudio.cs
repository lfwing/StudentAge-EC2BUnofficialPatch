using System;
using System.Collections.Generic;
using System.Reflection;
using Config;
using EC2BUnofficialPatch.Core;

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

        internal static bool TryResolve(
            int id,
            bool preview,
            out PerformanceAudioPlan plan,
            out string error)
        {
            plan = null;
            error = null;

            if (BetterAudioBridge.TryResolve(
                    id,
                    preview,
                    out bool bridgeAvailable,
                    out string path,
                    out float volume,
                    out int audioType,
                    out string bridgeError))
            {
                if (audioType == 2)
                {
                    error = $"audio={id} 在 BetterAudio 中注册为音效(type=2)，5002 只接受音乐(type=1)。";
                    return false;
                }

                plan = new PerformanceAudioPlan(id, path, volume);
                return true;
            }

            if (bridgeAvailable)
            {
                error = string.IsNullOrWhiteSpace(bridgeError)
                    ? $"BetterAudio 无法解析 audio={id}。"
                    : bridgeError;
                return false;
            }

            if (Cfg.AudioCfgMap == null ||
                !Cfg.AudioCfgMap.TryGetValue(id, out AudioCfg audioCfg) ||
                audioCfg == null ||
                string.IsNullOrWhiteSpace(audioCfg.url))
            {
                error = $"audio={id} 未在当前原版 AudioCfg 或 BetterAudio 中注册。";
                return false;
            }

            if (audioCfg.type == 2)
            {
                error = $"audio={id} 在原版 AudioCfg 中是音效(type=2)，5002 只接受音乐(type=1)。";
                return false;
            }

            string originalPath = AudioMgrEx.FormatUrl(audioCfg.url);
            if (string.IsNullOrWhiteSpace(originalPath))
            {
                error = $"audio={id} 的原版音频路径为空。";
                return false;
            }

            plan = new PerformanceAudioPlan(
                id,
                originalPath,
                audioCfg.volumn > 0f ? Math.Min(audioCfg.volumn, 1f) : 1f);
            return true;
        }
    }

    internal sealed class BetterAudioPauseLease
    {
        internal object Controller { get; set; }
        internal object Session { get; set; }
        internal long SessionToken { get; set; }
    }

    internal static class BetterAudioBridge
    {
        private const string ControllerTypeName =
            "LFBetterAudio.Runtime.BetterAudioController";
        private const string ResolverTypeName =
            "LFBetterAudio.Audio.AudioResolver";
        private const string ContextTypeName =
            "LFBetterAudio.Runtime.AudioResolveContext";
        private const string ChannelTypeName =
            "LFBetterAudio.Runtime.TalkChannel";

        internal static bool TryResolve(
            int id,
            bool preview,
            out bool bridgeAvailable,
            out string path,
            out float volume,
            out int audioType,
            out string error)
        {
            bridgeAvailable = false;
            path = null;
            volume = 1f;
            audioType = 1;
            error = null;

            Type resolverType = FindLoadedType(ResolverTypeName);
            Type contextType = FindLoadedType(ContextTypeName);
            Type channelType = FindLoadedType(ChannelTypeName);
            if (resolverType == null || contextType == null || channelType == null)
            {
                return false;
            }

            bridgeAvailable = true;
            try
            {
                object context = Activator.CreateInstance(contextType);
                PropertyInfo channelProperty = contextType.GetProperty("Channel");
                channelProperty?.SetValue(
                    context,
                    Enum.Parse(channelType, preview ? "Preview" : "Runtime"),
                    null);

                MethodInfo resolve = resolverType.GetMethod(
                    "TryResolve",
                    BindingFlags.Public | BindingFlags.Static);
                if (resolve == null)
                {
                    error = "当前 BetterAudio 版本未提供兼容的 AudioResolver.TryResolve。";
                    return false;
                }

                object[] arguments = { id, context, null, null };
                bool success = (bool)resolve.Invoke(null, arguments);
                if (!success || arguments[2] == null)
                {
                    error = arguments[3] as string ?? $"BetterAudio 无法解析 audio={id}。";
                    return false;
                }

                object resolved = arguments[2];
                Type resolvedType = resolved.GetType();
                path = resolvedType.GetProperty("AudioPath")?.GetValue(resolved, null) as string;
                object rawVolume = resolvedType.GetProperty("Volume")?.GetValue(resolved, null);
                object rawType = resolvedType.GetProperty("AudioType")?.GetValue(resolved, null);
                volume = rawVolume == null ? 1f : Convert.ToSingle(rawVolume);
                audioType = rawType == null ? 1 : Convert.ToInt32(rawType);

                if (string.IsNullOrWhiteSpace(path))
                {
                    error = $"BetterAudio 已识别 audio={id}，但没有返回可播放路径。";
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                error = $"BetterAudio 兼容解析失败：{GetBaseMessage(exception)}";
                return false;
            }
        }

        internal static BetterAudioPauseLease PauseActiveMusic()
        {
            try
            {
                Type controllerType = FindLoadedType(ControllerTypeName);
                object controller = controllerType?
                    .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?
                    .GetValue(null, null);
                object session = controllerType?
                    .GetProperty("ActiveSession", BindingFlags.Public | BindingFlags.Instance)?
                    .GetValue(controller, null);
                if (controller == null || session == null)
                {
                    return null;
                }

                Type sessionType = session.GetType();
                if (ReadBool(sessionType, session, "IsCancelled") ||
                    ReadBool(sessionType, session, "IsPaused") ||
                    ReadBool(sessionType, session, "PendingPauseAfterLoad"))
                {
                    return null;
                }

                bool loading = ReadBool(sessionType, session, "IsLoading");
                bool playing = ReadBool(sessionType, session, "IsPlaying");
                if (!loading && !playing)
                {
                    return null;
                }

                MethodInfo pause = controllerType.GetMethod(
                    "PauseActiveMusic",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (pause == null)
                {
                    PatchLog.Warning("5002屏幕滚动歌词扩展-当前 BetterAudio 版本不支持自动暂停接口。");
                    return null;
                }

                pause.Invoke(controller, new object[] { false });
                return new BetterAudioPauseLease
                {
                    Controller = controller,
                    Session = session,
                    SessionToken = ReadLong(sessionType, session, "Token")
                };
            }
            catch (Exception exception)
            {
                PatchLog.Warning(
                    $"5002屏幕滚动歌词扩展-自动暂停 BetterAudio 失败：{GetBaseMessage(exception)}");
                return null;
            }
        }

        internal static void Resume(BetterAudioPauseLease lease)
        {
            if (lease == null || lease.Controller == null || lease.Session == null)
            {
                return;
            }

            try
            {
                Type controllerType = lease.Controller.GetType();
                object currentController = controllerType
                    .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?
                    .GetValue(null, null);
                object currentSession = controllerType
                    .GetProperty("ActiveSession", BindingFlags.Public | BindingFlags.Instance)?
                    .GetValue(lease.Controller, null);
                if (!ReferenceEquals(currentController, lease.Controller) ||
                    !ReferenceEquals(currentSession, lease.Session) ||
                    ReadLong(lease.Session.GetType(), lease.Session, "Token") != lease.SessionToken)
                {
                    return;
                }

                MethodInfo resume = controllerType.GetMethod(
                    "ResumeActiveMusic",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                resume?.Invoke(lease.Controller, new object[] { false });
            }
            catch (Exception exception)
            {
                PatchLog.Warning(
                    $"5002屏幕滚动歌词扩展-恢复 BetterAudio 失败：{GetBaseMessage(exception)}");
            }
        }

        private static bool ReadBool(Type type, object instance, string propertyName)
        {
            object value = type.GetProperty(propertyName)?.GetValue(instance, null);
            return value != null && Convert.ToBoolean(value);
        }

        private static long ReadLong(Type type, object instance, string propertyName)
        {
            object value = type.GetProperty(propertyName)?.GetValue(instance, null);
            return value == null ? 0L : Convert.ToInt64(value);
        }

        private static string GetBaseMessage(Exception exception)
        {
            Exception current = exception;
            while (current.InnerException != null)
            {
                current = current.InnerException;
            }

            return current.Message;
        }

        private static Type FindLoadedType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type;
                try
                {
                    type = assembly.GetType(fullName, false);
                }
                catch
                {
                    continue;
                }

                if (type != null)
                {
                    return type;
                }
            }

            return Type.GetType($"{fullName}, LFBetterAudio", false);
        }
    }
}
