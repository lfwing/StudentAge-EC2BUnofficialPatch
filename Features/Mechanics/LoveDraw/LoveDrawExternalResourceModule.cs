using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using EC2BUnofficialPatch.Core;
using EC2BUnofficialPatch.Workshop;
using HarmonyLib;
using Sdk;
using UnityEngine;
using UnityEngine.Video;
using View.Love;
using UnityObject = UnityEngine.Object;

namespace EC2BUnofficialPatch.Features.Mechanics.LoveDraw
{
    /// <summary>
    /// 让原版 LoveDrawCfg 的 img/video 字段同时具备外置资源解析能力。
    /// 原版 Addressables 资源仍由 ResMgr 处理；命中 LoveDraw 目录的资源则走文件系统。
    /// </summary>
    internal sealed class LoveDrawExternalResourceModule : IPluginModule
    {
        private static readonly IReadOnlyList<ModuleLogItem> Items = new[]
        {
            new ModuleLogItem("机制", "情侣画修复")
        };

        public string Key => "mechanics.lovedraw.external";

        public IReadOnlyList<ModuleLogItem> LogItems => Items;

        public void Load(Harmony harmony, PluginServices services)
        {
            LoveDrawExternalResourcePatches.Initialize(services);

            MethodInfo onOpen = AccessTools.Method(typeof(PaintView), "OnOpen")
                ?? throw new MissingMethodException(typeof(PaintView).FullName, "OnOpen");
            MethodInfo onLoadVideo = AccessTools.Method(typeof(PaintView), "OnLoadVideo")
                ?? throw new MissingMethodException(typeof(PaintView).FullName, "OnLoadVideo");
            MethodInfo closeView = AccessTools.Method(typeof(PaintView), "CloseView", Type.EmptyTypes)
                ?? throw new MissingMethodException(typeof(PaintView).FullName, "CloseView");

            harmony.Patch(
                onOpen,
                transpiler: new HarmonyMethod(
                    typeof(LoveDrawExternalResourcePatches),
                    nameof(LoveDrawExternalResourcePatches.OnOpenTranspiler)));
            harmony.Patch(
                onLoadVideo,
                transpiler: new HarmonyMethod(
                    typeof(LoveDrawExternalResourcePatches),
                    nameof(LoveDrawExternalResourcePatches.OnLoadVideoTranspiler)));
            harmony.Patch(
                closeView,
                prefix: new HarmonyMethod(
                    typeof(LoveDrawExternalResourcePatches),
                    nameof(LoveDrawExternalResourcePatches.CloseViewPrefix)));

            PatchLog.Registration(
                "机制模块-情侣画外置资源索引完成：" +
                $"directories={services.ResourceIndex.LoveDrawDirectories.Count}, " +
                $"images={services.ResourceIndex.LoveDrawImageCount}, " +
                $"videos={services.ResourceIndex.LoveDrawVideoCount}");

            foreach (ResourceConflict conflict in services.ResourceIndex.Conflicts.Where(item =>
                item.RelativePath.StartsWith("LoveDraw/", StringComparison.OrdinalIgnoreCase)))
            {
                PatchLog.Warning(
                    "机制模块-情侣画外置资源存在同相对路径：" +
                    $"key={conflict.RelativePath}, first={conflict.Selected.FullPath}, " +
                    $"other={conflict.Ignored.FullPath}；未使用 Mods/<packageId>/... 精确指定来源时将拒绝歧义解析");
            }
        }
    }

    internal static class LoveDrawExternalResourcePatches
    {
        private static readonly ConditionalWeakTable<PaintView, ExternalVideoState> VideoStates =
            new ConditionalWeakTable<PaintView, ExternalVideoState>();

        private static readonly FieldInfo PaintField = AccessTools.Field(typeof(PaintView), "paint");
        private static readonly MethodInfo OnLoadImageMethod =
            AccessTools.Method(typeof(PaintView), "OnLoadImg", new[] { typeof(Texture2D) });
        private static readonly MethodInfo VideoCompletedMethod =
            AccessTools.Method(typeof(PaintView), "Video_bg_loopPointReached", new[] { typeof(VideoPlayer) });

        private static readonly MethodInfo OriginalVideoLoadMethod = GetLoadAsyncMethod(typeof(VideoClip));
        private static readonly MethodInfo OriginalTextureLoadMethod = GetLoadAsyncMethod(typeof(Texture2D));
        private static readonly MethodInfo ExternalVideoLoadMethod = AccessTools.Method(
            typeof(LoveDrawExternalResourcePatches),
            nameof(LoadVideoAsync));
        private static readonly MethodInfo ExternalTextureLoadMethod = AccessTools.Method(
            typeof(LoveDrawExternalResourcePatches),
            nameof(LoadTextureAsync));

        private static PluginServices _services;

        internal static void Initialize(PluginServices services)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            if (PaintField == null || OnLoadImageMethod == null || VideoCompletedMethod == null)
            {
                throw new MissingMemberException("PaintView 情侣画资源字段或回调方法不存在。游戏版本可能已变化。");
            }

            if (OriginalVideoLoadMethod == null ||
                OriginalTextureLoadMethod == null ||
                ExternalVideoLoadMethod == null ||
                ExternalTextureLoadMethod == null)
            {
                throw new MissingMethodException("情侣画资源加载桥接方法解析失败。");
            }
        }

        internal static IEnumerable<CodeInstruction> OnOpenTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = instructions.ToList();
            int videoReplacements = ReplaceCalls(
                codes,
                OriginalVideoLoadMethod,
                ExternalVideoLoadMethod);
            int textureReplacements = ReplaceCalls(
                codes,
                OriginalTextureLoadMethod,
                ExternalTextureLoadMethod);

            if (videoReplacements != 1 || textureReplacements != 1)
            {
                throw new InvalidOperationException(
                    "PaintView.OnOpen 资源加载点数量不符合预期：" +
                    $"video={videoReplacements}, texture={textureReplacements}");
            }

            return codes;
        }

        internal static IEnumerable<CodeInstruction> OnLoadVideoTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = instructions.ToList();
            int replacements = ReplaceCalls(
                codes,
                OriginalTextureLoadMethod,
                ExternalTextureLoadMethod);
            if (replacements != 1)
            {
                throw new InvalidOperationException(
                    "PaintView.OnLoadVideo 图片加载点数量不符合预期：" +
                    $"texture={replacements}");
            }

            return codes;
        }

        internal static void CloseViewPrefix(PaintView __instance)
        {
            CleanupVideoState(__instance, null);
        }

        /// <summary>
        /// 签名与 ResMgr.LoadAsync&lt;VideoClip&gt; 完全一致，供 IL 调用点直接替换。
        /// </summary>
        internal static void LoadVideoAsync(
            string path,
            Action<VideoClip> completedCallback,
            Action<float> loadingCallback = null,
            bool useResource = false)
        {
            PaintView view = completedCallback?.Target as PaintView;
            string fullPath = null;
            string resolveReason = _services == null
                ? "PluginServices 未初始化"
                : view == null
                    ? "无法从视频加载回调定位 PaintView"
                    : null;

            if (_services == null ||
                view == null ||
                !_services.ResourceIndex.TryResolveLoveDrawVideo(path, out fullPath, out resolveReason))
            {
                ResetOfficialVideoSource(view);
                if (ResourceIndex.LooksLikeExplicitLoveDrawPath(path))
                {
                    PatchLog.Warning(
                        "机制模块-情侣画外置视频未命中，将尝试原版资源：" +
                        $"cfg={path ?? "<null>"}, reason={resolveReason ?? "<unknown>"}");
                }

                ResMgr.LoadAsync(path, completedCallback, loadingCallback, useResource);
                return;
            }

            try
            {
                PatchLog.Info($"机制模块-情侣画外置视频调用：cfg={path}, file={fullPath}");
                StartExternalVideo(view, fullPath, loadingCallback);
            }
            catch (Exception exception)
            {
                PatchLog.Exception(
                    "机制模块-情侣画外置视频启动失败，将回退原版资源：" +
                    $"cfg={path}, file={fullPath}",
                    exception);
                CleanupVideoState(view, null);
                ResetOfficialVideoSource(view);
                ResMgr.LoadAsync(path, completedCallback, loadingCallback, useResource);
            }
        }

        /// <summary>
        /// 签名与 ResMgr.LoadAsync&lt;Texture2D&gt; 完全一致，供 IL 调用点直接替换。
        /// </summary>
        internal static void LoadTextureAsync(
            string path,
            Action<Texture2D> completedCallback,
            Action<float> loadingCallback = null,
            bool useResource = false)
        {
            string fullPath = null;
            string resolveReason = _services == null ? "PluginServices 未初始化" : null;

            if (_services == null ||
                !_services.ResourceIndex.TryResolveLoveDrawImage(path, out fullPath, out resolveReason))
            {
                if (ResourceIndex.LooksLikeExplicitLoveDrawPath(path))
                {
                    PatchLog.Warning(
                        "机制模块-情侣画外置底图未命中，将尝试原版资源：" +
                        $"cfg={path ?? "<null>"}, reason={resolveReason ?? "<unknown>"}");
                }

                ResMgr.LoadAsync(path, completedCallback, loadingCallback, useResource);
                return;
            }

            Texture2D sourceTexture = null;
            try
            {
                PatchLog.Info($"机制模块-情侣画外置底图调用：cfg={path}, file={fullPath}");
                byte[] bytes = File.ReadAllBytes(fullPath);
                sourceTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    name = "EC2B:LoveDraw:" + Path.GetFileName(fullPath),
                    hideFlags = HideFlags.HideAndDontSave
                };

                if (!ImageConversion.LoadImage(sourceTexture, bytes, false))
                {
                    throw new InvalidDataException("Unity 无法解码该图片。仅支持 PNG、JPG、JPEG。");
                }

                loadingCallback?.Invoke(1f);
                completedCallback?.Invoke(sourceTexture);
                PatchLog.Info(
                    "机制模块-情侣画已加载外置底图：" +
                    $"cfg={path}, file={fullPath}, size={sourceTexture.width}x{sourceTexture.height}");
            }
            catch (Exception exception)
            {
                PatchLog.Exception(
                    "机制模块-情侣画外置底图加载失败：" +
                    $"cfg={path}, file={fullPath}",
                    exception);
            }
            finally
            {
                if (sourceTexture != null)
                {
                    UnityObject.Destroy(sourceTexture);
                }
            }
        }

        private static void StartExternalVideo(
            PaintView view,
            string fullPath,
            Action<float> loadingCallback)
        {
            VideoPlayer player = view.video_bg;
            if (player == null)
            {
                throw new MissingComponentException("PaintView.video_bg 不存在。");
            }

            CleanupVideoState(view, null);

            ValueTuple<string, string, string, List<int>> paint =
                (ValueTuple<string, string, string, List<int>>)PaintField.GetValue(view);
            string imagePath = "textures/paint/" + paint.Item2;
            LoadTextureAsync(
                imagePath,
                texture => InvokeOnLoadImage(view, texture),
                null,
                false);

            ExternalVideoState state = new ExternalVideoState(view, player, fullPath);
            VideoStates.Add(view, state);
            state.Attach();

            player.Stop();
            player.clip = null;
            player.source = VideoSource.Url;
            player.url = new Uri(Path.GetFullPath(fullPath)).AbsoluteUri;
            player.isLooping = false;
            player.gameObject.SetActive(true);
            player.Prepare();

            loadingCallback?.Invoke(1f);
            PatchLog.Info(
                "机制模块-情侣画已接管外置视频：" +
                $"file={fullPath}, image={paint.Item2 ?? "<null>"}");
        }

        private static void InvokeOnLoadImage(PaintView view, Texture2D texture)
        {
            if (view == null || texture == null)
            {
                return;
            }

            try
            {
                OnLoadImageMethod.Invoke(view, new object[] { texture });
            }
            catch (TargetInvocationException exception)
            {
                throw exception.InnerException ?? exception;
            }
        }

        private static void CompleteVideoAfterError(
            PaintView view,
            VideoPlayer player,
            string reason)
        {
            if (view == null)
            {
                return;
            }

            PatchLog.Error(
                "机制模块-情侣画外置视频播放失败，已跳过视频并进入绘画阶段：" +
                $"file={player?.url ?? "<null>"}, reason={reason}");

            try
            {
                VideoCompletedMethod.Invoke(view, new object[] { player });
            }
            catch (Exception exception)
            {
                PatchLog.Exception("机制模块-情侣画视频失败回退处理异常", exception);
            }
        }

        private static void ResetOfficialVideoSource(PaintView view)
        {
            VideoPlayer player = view?.video_bg;
            if (player == null)
            {
                return;
            }

            CleanupVideoState(view, null);
            player.Stop();
            player.url = string.Empty;
            player.source = VideoSource.VideoClip;
        }

        private static void CleanupVideoState(
            PaintView view,
            ExternalVideoState expectedState)
        {
            if (view == null || !VideoStates.TryGetValue(view, out ExternalVideoState current))
            {
                return;
            }

            if (expectedState != null && !ReferenceEquals(current, expectedState))
            {
                return;
            }

            current.Detach();
            VideoStates.Remove(view);
        }

        private static int ReplaceCalls(
            IEnumerable<CodeInstruction> instructions,
            MethodInfo original,
            MethodInfo replacement)
        {
            int count = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (!instruction.Calls(original))
                {
                    continue;
                }

                instruction.operand = replacement;
                count++;
            }

            return count;
        }

        private static MethodInfo GetLoadAsyncMethod(Type assetType)
        {
            MethodInfo definition = typeof(ResMgr)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .SingleOrDefault(method =>
                    method.Name == nameof(ResMgr.LoadAsync) &&
                    method.IsGenericMethodDefinition &&
                    method.GetParameters().Length == 4);
            return definition?.MakeGenericMethod(assetType);
        }

        private sealed class ExternalVideoState
        {
            private readonly PaintView _view;
            private readonly VideoPlayer _player;
            private readonly string _fullPath;
            private bool _attached;
            private bool _finished;

            internal ExternalVideoState(PaintView view, VideoPlayer player, string fullPath)
            {
                _view = view;
                _player = player;
                _fullPath = fullPath;
            }

            internal void Attach()
            {
                if (_attached)
                {
                    return;
                }

                _attached = true;
                _player.prepareCompleted += OnPrepared;
                _player.errorReceived += OnError;
                _player.loopPointReached += OnCompleted;
            }

            internal void Detach()
            {
                if (!_attached)
                {
                    return;
                }

                _attached = false;
                if (_player != null)
                {
                    _player.prepareCompleted -= OnPrepared;
                    _player.errorReceived -= OnError;
                    _player.loopPointReached -= OnCompleted;
                }
            }

            private void OnPrepared(VideoPlayer source)
            {
                if (_finished || source == null)
                {
                    return;
                }

                try
                {
                    source.Play();
                    PatchLog.Debug(
                        "机制模块-情侣画外置视频开始播放：" +
                        $"file={_fullPath}, duration={source.length:0.###}");
                }
                catch (Exception exception)
                {
                    OnError(source, exception.Message);
                }
            }

            private void OnError(VideoPlayer source, string message)
            {
                if (_finished)
                {
                    return;
                }

                _finished = true;
                CleanupVideoState(_view, this);
                if (source != null)
                {
                    source.Stop();
                }

                CompleteVideoAfterError(_view, source, message);
            }

            private void OnCompleted(VideoPlayer source)
            {
                if (_finished)
                {
                    return;
                }

                _finished = true;
                CleanupVideoState(_view, this);
                PatchLog.Info($"机制模块-情侣画外置视频播放完成：file={_fullPath}");
            }
        }
    }
}
