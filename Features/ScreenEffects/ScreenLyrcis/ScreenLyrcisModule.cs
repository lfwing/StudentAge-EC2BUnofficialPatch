using System;
using System.Collections.Generic;
using System.Reflection;
using EC2BUnofficialPatch.Core;
using HarmonyLib;
using Sdk;
using View.Evt;

namespace EC2BUnofficialPatch.Features.ScreenEffects.ScreenLyrcis
{
    internal sealed class ScreenLyrcisModule : IPluginModule
    {
        private static LyricRegistry _registry;
        private static MethodInfo _newSpeedUp;
        private static MethodInfo _newHideLyrics;
        private static MethodInfo _previewShowCurrentText;

        private static readonly IReadOnlyList<ModuleLogItem> Items = new[]
        {
            new ModuleLogItem("屏幕特效", "5002-屏幕滚动歌词扩展")
        };

        public string Key => "screen.5002";

        public IReadOnlyList<ModuleLogItem> LogItems => Items;

        public void Load(Harmony harmony, PluginServices services)
        {
            _registry = LyricRegistry.Load(services.ResourceIndex.LyricFiles);
            foreach (string error in _registry.Errors)
            {
                PatchLog.Error($"5002屏幕滚动歌词扩展-配置错误：{error}");
            }
            foreach (LyricConflict conflict in _registry.Conflicts)
            {
                PatchLog.Error(
                    $"5002屏幕滚动歌词扩展-ID 冲突：lyrics id={conflict.Id}, " +
                    $"first={conflict.Selected.SourcePath}, second={conflict.Ignored.SourcePath}。" +
                    "为避免跨 Mod 抢占，已禁用该 ID。");
            }
            PatchLog.Registration(
                $"屏幕特效模块-5002屏幕滚动歌词注册完成：歌词={_registry.Count}, " +
                $"冲突={_registry.Conflicts.Count}, 错误={_registry.Errors.Count}");

            _newSpeedUp = RequireMethod(typeof(NewTalkView), "SpeedUp");
            _newHideLyrics = RequireMethod(typeof(NewTalkView), "HideLyrics");
            _previewShowCurrentText = RequireMethod(typeof(PreviewTalkView), "ShowCurTxt");

            TalkViewAccessor.Validate(typeof(NewTalkView));
            TalkViewAccessor.Validate(typeof(PreviewTalkView));

            Patch(
                harmony,
                typeof(NewTalkView),
                "ShowLyrics",
                nameof(NewShowLyricsPrefix),
                null);
            Patch(
                harmony,
                typeof(NewTalkView),
                "HideLyrics",
                nameof(NewHideLyricsPrefix),
                null);
            Patch(
                harmony,
                typeof(NewTalkView),
                "OnClose",
                nameof(NewOnClosePrefix),
                null);
            Patch(
                harmony,
                typeof(PreviewTalkView),
                "PlayBgEffect",
                nameof(PreviewPlayBgEffectPrefix),
                null);
            Patch(
                harmony,
                typeof(PreviewTalkView),
                "ShowCurTxt",
                nameof(PreviewShowCurTxtPrefix),
                null);
            Patch(
                harmony,
                typeof(PreviewTalkView),
                "OnClose",
                nameof(PreviewOnClosePrefix),
                null);
        }

        private static bool NewShowLyricsPrefix(
            NewTalkView __instance,
            ref bool __result)
        {
            if (!TryGetCurrentLyric(__instance, out LyricEntry lyric))
            {
                LyricsPresenter.Release(__instance, __instance);
                return true;
            }

            bool started = LyricsPresenter.TryStart(
                __instance,
                __instance,
                lyric,
                false,
                () =>
                {
                    __instance.talkState = TalkState.Lyrics;
                    _newSpeedUp.Invoke(__instance, new object[] { false });
                },
                () => _newHideLyrics.Invoke(__instance, null));
            if (!started)
            {
                return true;
            }

            PatchLog.Info(
                $"屏幕特效模块-5002屏幕滚动歌词调用：lyricsId={lyric.id}, " +
                $"audio={(lyric.audio.HasValue ? lyric.audio.Value.ToString() : "<none>")}, preview=false");
            __result = false;
            return false;
        }

        private static void NewHideLyricsPrefix(NewTalkView __instance)
        {
            LyricsPresenter.BeforeHide(__instance, __instance);
        }

        private static void NewOnClosePrefix(NewTalkView __instance)
        {
            LyricsPresenter.Release(__instance, __instance);
        }

        private static bool PreviewPlayBgEffectPrefix(PreviewTalkView __instance)
        {
            if (!TryGetLyricCommand(__instance, out int lyricId))
            {
                LyricsPresenter.Release(__instance, __instance);
                return true;
            }

            LyricEntry lyric;
            if (lyricId <= 0 || !_registry.TryGet(lyricId, out lyric))
            {
                lyric = new LyricEntry
                {
                    id = lyricId,
                    text = __instance.txtex_lyrics.text,
                    PreserveExistingStyle = true
                };
            }

            bool started = LyricsPresenter.TryStart(
                __instance,
                __instance,
                lyric,
                true,
                () =>
                {
                    __instance.talkState = TalkState.Lyrics;
                },
                () => CompletePreview(__instance));
            if (started)
            {
                PatchLog.Info(
                    $"屏幕特效模块-5002屏幕滚动歌词调用：lyricsId={lyric.id}, " +
                    $"audio={(lyric.audio.HasValue ? lyric.audio.Value.ToString() : "<none>")}, preview=true");
            }
            return !started;
        }

        private static bool PreviewShowCurTxtPrefix(PreviewTalkView __instance)
        {
            return !LyricsPresenter.IsActive(__instance);
        }

        private static void PreviewOnClosePrefix(PreviewTalkView __instance)
        {
            LyricsPresenter.Release(__instance, __instance);
        }

        private static void CompletePreview(PreviewTalkView view)
        {
            view.talkState = TalkState.AnimEnd;
            view.scroll_lyrics.gameObject.SetActive(false);
            view.group_role.gameObject.SetActive(true);
            AudioMgrEx.PauseNpcSound();
            _previewShowCurrentText.Invoke(view, null);
        }

        private static bool TryGetCurrentLyric(object talkView, out LyricEntry lyric)
        {
            lyric = null;
            if (!TryGetLyricCommand(talkView, out int lyricId) || lyricId <= 0)
            {
                return false;
            }

            return _registry.TryGet(lyricId, out lyric);
        }

        private static bool TryGetLyricCommand(object talkView, out int lyricId)
        {
            lyricId = 0;
            List<float> screenEffect = TalkViewAccessor.GetScreenEffect(talkView);
            if (screenEffect == null ||
                screenEffect.Count == 0 ||
                (int)screenEffect[0] != CommandIds.ScreenLyrcis)
            {
                return false;
            }

            if (screenEffect.Count > 1)
            {
                lyricId = (int)screenEffect[1];
            }

            return true;
        }

        private static void Patch(
            Harmony harmony,
            Type targetType,
            string targetName,
            string prefixName,
            string postfixName)
        {
            MethodInfo target = RequireMethod(targetType, targetName);
            HarmonyMethod prefix = prefixName == null
                ? null
                : new HarmonyMethod(
                    AccessTools.Method(typeof(ScreenLyrcisModule), prefixName));
            HarmonyMethod postfix = postfixName == null
                ? null
                : new HarmonyMethod(
                    AccessTools.Method(typeof(ScreenLyrcisModule), postfixName));
            harmony.Patch(target, prefix, postfix);
        }

        private static MethodInfo RequireMethod(Type type, string name)
        {
            return AccessTools.Method(type, name)
                ?? throw new MissingMethodException(type.FullName, name);
        }
    }
}
