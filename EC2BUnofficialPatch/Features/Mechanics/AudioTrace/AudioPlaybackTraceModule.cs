using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using EC2BUnofficialPatch.Core;
using HarmonyLib;
using Sdk;
using UnityEngine;

namespace EC2BUnofficialPatch.Features.Mechanics.AudioTrace
{
    internal sealed class AudioPlaybackTraceModule : IPluginModule
    {
        private readonly bool _original;
        private readonly bool _betterAudio;
        private readonly bool _unity;
        private readonly IReadOnlyList<ModuleLogItem> _items;

        internal AudioPlaybackTraceModule(bool original, bool betterAudio, bool unity)
        {
            _original = original;
            _betterAudio = betterAudio;
            _unity = unity;

            List<ModuleLogItem> items = new List<ModuleLogItem>();
            if (_original)
                items.Add(new ModuleLogItem("机制", "音频播放监控-原版音频渠道"));
            if (_betterAudio)
                items.Add(new ModuleLogItem("机制", "音频播放监控-BetterAudio渠道"));
            if (_unity)
                items.Add(new ModuleLogItem("机制", "音频播放监控-unity底层渠道"));
            _items = items;
        }

        public string Key => "mechanics.audio-trace";
        public IReadOnlyList<ModuleLogItem> LogItems => _items;

        public void Load(Harmony harmony, PluginServices services)
        {
            AudioPlaybackTracePatches.Initialize();

            // 路径映射本身不会输出播放日志。只要任一渠道开启就保留该补丁，
            // 这样 Unity/BetterAudio 渠道也能尽可能拿到原版资源键。
            PatchRequired(
                harmony,
                AccessTools.Method(typeof(ResMgr), "LoadAudioAsync", new[]
                {
                    typeof(string), typeof(Action<AudioClip>), typeof(Action<float>), typeof(bool)
                }),
                nameof(AudioPlaybackTracePatches.LoadAudioAsyncPrefix));

            TryPatch(
                harmony,
                AccessTools.Method(typeof(ResMgr), "LoadExternAudioAsync", new[]
                {
                    typeof(string), typeof(Action<AudioClip>)
                }),
                nameof(AudioPlaybackTracePatches.LoadExternAudioAsyncPrefix));

            // 不补丁 ResMgr.LoadAsync<AudioClip>。Unity/Mono 会共享部分泛型方法实现，
            // 对封闭泛型实例安装 Harmony 补丁可能污染 LoadAsync<Texture2D>/LoadAsync<GameObject>
            // 等其它 T，造成 Addressables 以 AudioClip 类型错误请求非音频资源。
            // 原版资源路径只通过专用 LoadAudioAsync / LoadExternAudioAsync 建立映射；
            // BetterAudio 自身的字符串参数与 Unity AudioSource 作为其余渠道的路径/播放兜底。

            if (_original)
            {
                PatchRequired(
                    harmony,
                    AccessTools.Method(typeof(Channel), "Play", new[]
                    {
                        typeof(AudioClip), typeof(float), typeof(bool), typeof(Action), typeof(float)
                    }),
                    nameof(AudioPlaybackTracePatches.ChannelPlayPrefix));

                PatchRequired(
                    harmony,
                    AccessTools.Method(typeof(Channel), "PlayOneShot", new[]
                    {
                        typeof(AudioClip), typeof(float), typeof(float)
                    }),
                    nameof(AudioPlaybackTracePatches.ChannelPlayOneShotPrefix));
            }

            if (_unity)
            {
                TryPatchUnityAudioSource(harmony);
            }

            if (_betterAudio)
            {
                TryPatchBetterAudioAudioSource(harmony);
                PatchLog.Debug("机制模块-BetterAudio 音频渠道已使用实际 AudioSource 播放调用监控");
            }
        }

        private static void TryPatchUnityAudioSource(Harmony harmony)
        {
            TryPatch(harmony, AccessTools.Method(typeof(AudioSource), "Play", Type.EmptyTypes),
                nameof(AudioPlaybackTracePatches.AudioSourcePlayPrefix));
            TryPatch(harmony, AccessTools.Method(typeof(AudioSource), "Play", new[] { typeof(ulong) }),
                nameof(AudioPlaybackTracePatches.AudioSourcePlayPrefix));
            TryPatch(harmony, AccessTools.Method(typeof(AudioSource), "PlayDelayed", new[] { typeof(float) }),
                nameof(AudioPlaybackTracePatches.AudioSourcePlayPrefix));
            TryPatch(harmony, AccessTools.Method(typeof(AudioSource), "PlayScheduled", new[] { typeof(double) }),
                nameof(AudioPlaybackTracePatches.AudioSourcePlayPrefix));
            TryPatch(harmony, AccessTools.Method(typeof(AudioSource), "PlayOneShot", new[] { typeof(AudioClip) }),
                nameof(AudioPlaybackTracePatches.AudioSourcePlayOneShotSimplePrefix));
            TryPatch(harmony, AccessTools.Method(typeof(AudioSource), "PlayOneShot", new[] { typeof(AudioClip), typeof(float) }),
                nameof(AudioPlaybackTracePatches.AudioSourcePlayOneShotPrefix));
        }

        private static void TryPatchBetterAudioAudioSource(Harmony harmony)
        {
            TryPatch(harmony, AccessTools.Method(typeof(AudioSource), "Play", Type.EmptyTypes),
                nameof(AudioPlaybackTracePatches.BetterAudioSourcePlayPrefix));
            TryPatch(harmony, AccessTools.Method(typeof(AudioSource), "Play", new[] { typeof(ulong) }),
                nameof(AudioPlaybackTracePatches.BetterAudioSourcePlayPrefix));
            TryPatch(harmony, AccessTools.Method(typeof(AudioSource), "PlayDelayed", new[] { typeof(float) }),
                nameof(AudioPlaybackTracePatches.BetterAudioSourcePlayPrefix));
            TryPatch(harmony, AccessTools.Method(typeof(AudioSource), "PlayScheduled", new[] { typeof(double) }),
                nameof(AudioPlaybackTracePatches.BetterAudioSourcePlayPrefix));
            TryPatch(harmony, AccessTools.Method(typeof(AudioSource), "PlayOneShot", new[] { typeof(AudioClip) }),
                nameof(AudioPlaybackTracePatches.BetterAudioSourcePlayOneShotSimplePrefix));
            TryPatch(harmony, AccessTools.Method(typeof(AudioSource), "PlayOneShot", new[] { typeof(AudioClip), typeof(float) }),
                nameof(AudioPlaybackTracePatches.BetterAudioSourcePlayOneShotPrefix));
        }

        private static void PatchRequired(Harmony harmony, MethodInfo target, string prefix)
        {
            if (target == null)
                throw new MissingMethodException($"音频追踪目标方法不存在：{prefix}");
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(AudioPlaybackTracePatches), prefix));
        }

        private static void TryPatch(Harmony harmony, MethodInfo target, string prefix)
        {
            if (target == null)
                return;
            try
            {
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(AudioPlaybackTracePatches), prefix));
            }
            catch (Exception exception)
            {
                PatchLog.Warning($"机制模块-音频监控方法补丁失败，已跳过：method={target}, reason={ModuleHost.GetReason(exception)}");
            }
        }
    }

    internal static class AudioPlaybackTracePatches
    {
        private static readonly object SyncRoot = new object();
        // AudioClip instance IDs may be reused after destruction. Keep metadata tied to
        // the managed clip, without retaining that clip (or its path) forever.
        private static ConditionalWeakTable<AudioClip, ClipPath> ClipPaths = new ConditionalWeakTable<AudioClip, ClipPath>();
        private const int LogWindowLimit = 512;
        private static HashSet<LogKey> CurrentLogs = new HashSet<LogKey>();
        private static HashSet<LogKey> PreviousLogs = new HashSet<LogKey>();
        private static int _logFrame;

        private sealed class ClipPath { internal string Value; }

        private readonly struct LogKey : IEquatable<LogKey>
        {
            private readonly string _channel, _entry;
            private readonly int _clip, _source;
            internal LogKey(string channel, string entry, int clip, int source)
            { _channel = channel; _entry = entry; _clip = clip; _source = source; }
            public bool Equals(LogKey other) => _clip == other._clip && _source == other._source &&
                _channel == other._channel && _entry == other._entry;
            public override bool Equals(object value) => value is LogKey other && Equals(other);
            public override int GetHashCode()
            {
                unchecked { return (((_channel?.GetHashCode() ?? 0) * 397 ^
                    (_entry?.GetHashCode() ?? 0)) * 397 ^ _clip) * 397 ^ _source; }
            }
        }

        internal static void Initialize()
        {
            lock (SyncRoot)
            {
                ClipPaths = new ConditionalWeakTable<AudioClip, ClipPath>();
                CurrentLogs.Clear();
                PreviousLogs.Clear();
                _logFrame = 0;
            }
        }

        public static void LoadAudioAsyncPrefix(string _path, ref Action<AudioClip> _compCallback)
        {
            WrapAudioCallback(_path, ref _compCallback);
        }

        public static void LoadExternAudioAsyncPrefix(string _path, ref Action<AudioClip> _callback)
        {
            WrapAudioCallback(_path, ref _callback);
        }

        private static void WrapAudioCallback(string path, ref Action<AudioClip> callback)
        {
            string requestedPath = NormalizePath(path);
            Action<AudioClip> original = callback;
            callback = clip =>
            {
                RegisterClipPath(clip, requestedPath);
                original?.Invoke(clip);
            };
        }

        public static void ChannelPlayPrefix(Channel __instance, AudioClip _clip, float _volumeScale, bool _isLoop)
        {
            if (!PluginConfig.AudioOriginalChannel.Value)
                return;
            LogClip("原版", "Channel.Play", __instance?.source, _clip, ResolveClipPath(_clip),
                $"loop={_isLoop}, volume={_volumeScale:0.###}");
        }

        public static void ChannelPlayOneShotPrefix(Channel __instance, AudioClip _clip, float _volumeScale, float _pitch)
        {
            if (!PluginConfig.AudioOriginalChannel.Value)
                return;
            LogClip("原版", "Channel.PlayOneShot", __instance?.source, _clip, ResolveClipPath(_clip),
                $"volume={_volumeScale:0.###}, pitch={_pitch:0.###}");
        }

        public static void AudioSourcePlayPrefix(AudioSource __instance, MethodBase __originalMethod)
        {
            if (!PluginConfig.AudioUnityChannel.Value || __instance == null || __instance.clip == null)
                return;
            string entry = "AudioSource." + (__originalMethod?.Name ?? "Play");
            LogClip("Unity", entry, __instance, __instance.clip,
                ResolveClipPath(__instance.clip), $"loop={__instance.loop}");
        }

        public static void AudioSourcePlayOneShotSimplePrefix(AudioSource __instance, AudioClip clip)
        {
            AudioSourcePlayOneShotPrefix(__instance, clip, 1f);
        }

        public static void AudioSourcePlayOneShotPrefix(AudioSource __instance, AudioClip clip, float volumeScale)
        {
            if (!PluginConfig.AudioUnityChannel.Value || clip == null)
                return;
            LogClip("Unity", "AudioSource.PlayOneShot", __instance, clip,
                ResolveClipPath(clip), $"volume={volumeScale:0.###}");
        }

        public static void BetterAudioSourcePlayPrefix(AudioSource __instance, MethodBase __originalMethod)
        {
            if (!PluginConfig.AudioBetterAudioChannel.Value || __instance == null || __instance.clip == null ||
                !TryGetBetterAudioCaller(out MethodBase caller))
                return;

            string entry = DescribeMethod(caller) + " -> AudioSource." + (__originalMethod?.Name ?? "Play");
            LogClip("BetterAudio", entry, __instance, __instance.clip, ResolveClipPath(__instance.clip),
                $"loop={__instance.loop}, pluginChannel=true");
        }

        public static void BetterAudioSourcePlayOneShotSimplePrefix(AudioSource __instance, AudioClip clip)
        {
            BetterAudioSourcePlayOneShotPrefix(__instance, clip, 1f);
        }

        public static void BetterAudioSourcePlayOneShotPrefix(AudioSource __instance, AudioClip clip, float volumeScale)
        {
            if (!PluginConfig.AudioBetterAudioChannel.Value || clip == null ||
                !TryGetBetterAudioCaller(out MethodBase caller))
                return;

            LogClip("BetterAudio", DescribeMethod(caller) + " -> AudioSource.PlayOneShot",
                __instance, clip, ResolveClipPath(clip), $"volume={volumeScale:0.###}, pluginChannel=true");
        }

        private static bool TryGetBetterAudioCaller(out MethodBase caller)
        {
            caller = null;
            var trace = new StackTrace(false);
            for (int i = 0; i < trace.FrameCount; i++)
            {
                StackFrame frame = trace.GetFrame(i);
                MethodBase method = frame?.GetMethod();
                if (!IsBetterAudioCallerType(method?.DeclaringType))
                    continue;
                caller = method;
                return true;
            }
            return false;
        }

        internal static bool IsBetterAudioCallerType(Type type)
        {
            if (type == null)
                return false;
            // Works for both merged and separate DLLs, including compiler-generated coroutine types.
            string name = type.FullName ?? string.Empty;
            return name.StartsWith("LFBetterAudio.", StringComparison.Ordinal);
        }

        private static string DescribeMethod(MethodBase method)
        {
            if (method == null)
                return "<unknown>";
            return (method.DeclaringType?.FullName ?? "<unknown>") + "." + method.Name;
        }

        private static void RegisterClipPath(AudioClip clip, string path)
        {
            if (clip == null || string.IsNullOrWhiteSpace(path))
                return;
            lock (SyncRoot)
            {
                if (!ClipPaths.TryGetValue(clip, out ClipPath stored))
                {
                    stored = new ClipPath();
                    ClipPaths.Add(clip, stored);
                }
                stored.Value = path;
            }
        }

        private static string ResolveClipPath(AudioClip clip)
        {
            if (clip == null)
                return "<unknown>";
            lock (SyncRoot)
            {
                if (ClipPaths.TryGetValue(clip, out ClipPath path))
                    return path.Value;
            }
            return $"<Unity内存/AssetBundle资源；未捕获加载路径，clip={clip.name}>";
        }

        private static void LogClip(string channel, string entry, AudioSource source, AudioClip clip, string path, string extra)
        {
            if (!TryBeginLog(channel, entry, clip != null ? clip.GetInstanceID() : 0,
                source != null ? source.GetInstanceID() : 0, Time.frameCount))
                return;
            string clipName = clip != null && !string.IsNullOrWhiteSpace(clip.name) ? clip.name : "<unknown>";
            string sourceName = source != null ? GetObjectPath(source.transform) : "<unknown>";
            string normalizedPath = string.IsNullOrWhiteSpace(path) ? "<unknown>" : path;
            PatchLog.Info($"机制模块-音频播放：channel={channel}, entry={entry}, clip={clipName}, path={normalizedPath}, source={sourceName}, {extra}");
        }

        internal static bool TryBeginLog(string channel, string entry, int clip, int source, int frame)
        {
            var key = new LogKey(channel, entry, clip, source);
            lock (SyncRoot)
            {
                if (frame != _logFrame)
                {
                    PreviousLogs.Clear();
                    if ((long)frame - _logFrame == 1)
                    {
                        var reusable = PreviousLogs;
                        PreviousLogs = CurrentLogs;
                        CurrentLogs = reusable;
                    }
                    else CurrentLogs.Clear();
                    _logFrame = frame;
                }
                if (CurrentLogs.Contains(key) || PreviousLogs.Contains(key)) return false;
                // Under an extreme debug burst, emit uncached entries rather than
                // growing memory indefinitely or hiding unrelated playback events.
                if (CurrentLogs.Count < LogWindowLimit) CurrentLogs.Add(key);
                return true;
            }
        }

        private static string GetObjectPath(Transform transform)
        {
            if (transform == null) return "<null>";
            List<string> names = new List<string>();
            while (transform != null)
            {
                names.Add(transform.name);
                transform = transform.parent;
            }
            names.Reverse();
            return string.Join("/", names);
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "<unknown>";
            try
            {
                if (Uri.TryCreate(path, UriKind.Absolute, out Uri uri) && uri.IsFile)
                    return uri.LocalPath;
                if (Path.IsPathRooted(path))
                    return Path.GetFullPath(path);
            }
            catch { }
            return path.Replace('\\', '/');
        }
    }
}
