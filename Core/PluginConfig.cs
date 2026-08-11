using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx.Configuration;

namespace EC2BUnofficialPatch.Core
{
    internal static class PluginConfig
    {
        internal static ConfigEntry<bool> ScreenDynamicWaitText { get; private set; }
        internal static ConfigEntry<bool> ScreenComicExtension { get; private set; }
        internal static ConfigEntry<bool> ScreenBackgroundEffects { get; private set; }
        internal static ConfigEntry<bool> ScreenPaper { get; private set; }
        internal static ConfigEntry<bool> ScreenLyrics { get; private set; }
        internal static ConfigEntry<bool> Action3003 { get; private set; }
        internal static ConfigEntry<bool> AnimeExtension { get; private set; }
        internal static ConfigEntry<bool> MapMoveEffects { get; private set; }
        internal static ConfigEntry<bool> LoveDrawExternalResources { get; private set; }
        internal static ConfigEntry<bool> MinigameMechanics { get; private set; }
        internal static ConfigEntry<bool> AudioOriginalChannel { get; private set; }
        internal static ConfigEntry<bool> AudioBetterAudioChannel { get; private set; }
        internal static ConfigEntry<bool> AudioUnityChannel { get; private set; }
        internal static ConfigEntry<bool> StaticPortraitOptimization { get; private set; }
        internal static ConfigEntry<bool> CGOptimization { get; private set; }
        internal static ConfigEntry<bool> ExamManualScore { get; private set; }
        internal static ConfigEntry<string> LoveTopicLimit { get; private set; }
        internal static ConfigEntry<bool> RelationEffects { get; private set; }
        internal static ConfigEntry<bool> RelationFocusCount { get; private set; }
        internal static ConfigEntry<bool> RoleAvailability { get; private set; }

        internal static void Initialize(ConfigFile config)
        {
            bool saveOnConfigSet = config.SaveOnConfigSet;
            config.SaveOnConfigSet = false;

            try
            {
                bool screenDynamicWaitText = ReadMigratedValue(
                    config,
                    "屏幕特效",
                    "4006黑屏特效显示文字扩展",
                    "屏幕特效",
                    "动态等待文字");
                bool screenComicExtension = ReadMigratedValue(
                    config,
                    "屏幕特效",
                    "4016漫画显示扩展",
                    "屏幕特效",
                    "漫画显示扩展");
                bool screenBackgroundEffects = ReadMigratedValue(
                    config,
                    "屏幕特效",
                    "屏幕特效扩展",
                    "屏幕特效",
                    "背景与屏幕特效");
                bool screenPaper = ReadMigratedValue(
                    config,
                    "屏幕特效",
                    "5001屏幕纸条扩展",
                    "屏幕特效",
                    "屏幕纸条扩展");
                bool screenLyrics = ReadMigratedValue(
                    config,
                    "屏幕特效",
                    "5002屏幕滚动歌词扩展",
                    "屏幕特效",
                    "屏幕滚动歌词扩展",
                    ReadRawValue(config, "屏幕特效", "歌词显示扩展", true));

                bool action3003 = ReadMigratedValue(
                    config,
                    "行动指令",
                    "3003修复",
                    "行动指令",
                    "行动3003扩展");

                bool legacyAnimeExtension = ReadMigratedValue(
                    config,
                    "效果",
                    "36动画相关修复",
                    "效果机制",
                    "动画资源扩展");
                bool animeExtension = ReadRawValue(
                    config,
                    "效果",
                    "36动画相关修复与扩展",
                    legacyAnimeExtension);
                bool mapMoveEffects = ReadMigratedValue(
                    config,
                    "效果",
                    "100,1地点移动修复",
                    "效果机制",
                    "地图移动效果");

                bool loveDrawExternalResources = ReadMigratedValue(
                    config,
                    "机制",
                    "情侣画修复",
                    "机制",
                    "情侣画外置资源");
                bool minigameMechanics = ReadMigratedValue(
                    config,
                    "机制",
                    "社交小游戏修复",
                    "机制",
                    "小游戏扩展");

                // 1.0.13 曾先使用“音频播放日志”，后改为“音频播放监控”单总开关，
                // 更早的试验版还存在三个分渠道开关。1.0.14 恢复三个渠道，按最接近
                // 的旧值逐层继承，避免升级后把玩家已经关闭的渠道重新打开。
                bool legacyAudioLog = ReadRawValue(config, "机制", "音频播放日志", true);
                bool legacyAudioMaster = ReadRawValue(config, "机制", "音频播放监控", legacyAudioLog);
                bool legacyAudioOriginal = ReadRawValue(
                    config,
                    "机制-音频播放日志",
                    "记录原版音频渠道",
                    legacyAudioMaster);
                bool legacyAudioBetterAudio = ReadRawValue(
                    config,
                    "机制-音频播放日志",
                    "记录BetterAudio渠道",
                    legacyAudioMaster);
                bool legacyAudioUnity = ReadRawValue(
                    config,
                    "机制-音频播放日志",
                    "记录Unity底层渠道",
                    legacyAudioMaster);

                bool oldAudioOriginal = ReadRawValue(config, "机制", "原版音频渠道", legacyAudioOriginal);
                bool oldAudioBetterAudio = ReadRawValue(config, "机制", "BetterAudio渠道", legacyAudioBetterAudio);
                bool oldAudioUnity = ReadRawValue(config, "机制", "unity底层渠道", legacyAudioUnity);
                bool audioOriginal = ReadRawValue(config, "机制", "监控原版音频渠道", oldAudioOriginal);
                bool audioBetterAudio = ReadRawValue(config, "机制", "监控BetterAudio音频渠道", oldAudioBetterAudio);
                bool audioUnity = ReadRawValue(config, "机制", "监控unity底层音频渠道", oldAudioUnity);

                bool staticPortraitOptimization = ReadMigratedValue(
                    config,
                    "优化",
                    "静态立绘优化",
                    "优化",
                    "静态立绘优化");
                bool cgOptimization = ReadMigratedValue(
                    config,
                    "优化",
                    "CG播放与图鉴排序优化",
                    "优化",
                    "CG播放优化");

                bool examManualScore = ReadRawValue(config, "优化", "普通考试允许手动输入成绩", true);
                string oldLoveTopicLimit = ReadRawString(config, "机制", "情侣话题每回合次数", "1");
                string loveTopicLimit = ReadRawString(config, "优化", "情侣话题每回合次数", oldLoveTopicLimit);
                bool legacyRelationCombined = ReadRawValue(config, "机制", "关系效果与关注人数修复", true);
                bool oldRelationEffects = ReadRawValue(config, "效果", "关系效果修复", legacyRelationCombined);
                bool relationEffects = ReadRawValue(config, "效果", "20关系效果修复与扩展", oldRelationEffects);
                bool oldRelationFocusCount = ReadRawValue(config, "机制", "关注人数修复", legacyRelationCombined);
                bool relationFocusCount = ReadRawValue(config, "优化", "关注人数统计优化", oldRelationFocusCount);
                bool oldRoleAvailability = ReadRawValue(config, "机制", "外置角色可用性", true);
                bool roleAvailability = ReadRawValue(config, "机制", "控制角色在列表显示", oldRoleAvailability);

                ResetConfigFile(config);

                ScreenDynamicWaitText = Bind(
                    config,
                    "屏幕特效",
                    "4006黑屏特效显示文字扩展",
                    screenDynamicWaitText,
                    "扩展 4006 黑屏过场显示文字指令。");
                ScreenComicExtension = Bind(
                    config,
                    "屏幕特效",
                    "4016漫画显示扩展",
                    screenComicExtension,
                    "扩展 4016 漫画指令，支持外置图片读取、规范校验与 Workshop 嗅探（本插件使用comic文件夹）。");
                ScreenBackgroundEffects = Bind(
                    config,
                    "屏幕特效",
                    "屏幕特效扩展",
                    screenBackgroundEffects,
                    "扩展屏幕黑白、屏幕打码等屏幕特效指令。");
                ScreenPaper = Bind(
                    config,
                    "屏幕特效",
                    "5001屏幕纸条扩展",
                    screenPaper,
                    "扩展 5001 屏幕纸条指令（本插件使用ScreenPaper文件夹）。");
                ScreenLyrics = Bind(
                    config,
                    "屏幕特效",
                    "5002屏幕滚动歌词扩展",
                    screenLyrics,
                    "扩展 5002 屏幕滚动歌词指令（本插件使用ScreenLyrcis文件夹）。");

                Action3003 = Bind(
                    config,
                    "行动指令",
                    "3003修复",
                    action3003,
                    "修复 3003 行动指令。");

                AnimeExtension = Bind(
                    config,
                    "效果",
                    "36动画相关修复与扩展",
                    animeExtension,
                    "针对 36 动画相关效果进行修复与扩展。");
                MapMoveEffects = Bind(
                    config,
                    "效果",
                    "100,1地点移动修复",
                    mapMoveEffects,
                    "针对 100,1 地点移动效果进行修复。");

                LoveDrawExternalResources = Bind(
                    config,
                    "机制",
                    "情侣画修复",
                    loveDrawExternalResources,
                    "支持情侣画外置底图/视频读取，且支持双cfg读取（本插件使用LoveDraw文件夹）。");
                MinigameMechanics = Bind(
                    config,
                    "机制",
                    "社交小游戏修复",
                    minigameMechanics,
                    "支持角色独立小游戏阶段、自定义小游戏注册与统一结算，且支持双cfg读取（本插件使用Minigame文件夹）。");
                AudioOriginalChannel = Bind(
                    config,
                    "机制",
                    "监控原版音频渠道",
                    audioOriginal,
                    "监控游戏原版 ResMgr/Channel 音频播放渠道并输出音频名称和可解析路径。");
                AudioBetterAudioChannel = Bind(
                    config,
                    "机制",
                    "监控BetterAudio音频渠道",
                    audioBetterAudio,
                    "监控 BetterAudio 插件的音频播放入口并输出音频名称和可解析路径。");
                AudioUnityChannel = Bind(
                    config,
                    "机制",
                    "监控unity底层音频渠道",
                    audioUnity,
                    "监控 Unity AudioSource 底层播放入口；可能与上层渠道同时记录同一次播放。");

                StaticPortraitOptimization = Bind(
                    config,
                    "优化",
                    "静态立绘优化",
                    staticPortraitOptimization,
                    "优化静态立绘在切换表情等情况下的跳变显示问题。");
                CGOptimization = Bind(
                    config,
                    "优化",
                    "CG播放与图鉴排序优化",
                    cgOptimization,
                    "为连续播放的CG自动添加过渡效果；自动排序MOD CG图鉴。");

                ExamManualScore = Bind(
                    config,
                    "优化",
                    "普通考试允许手动输入成绩",
                    examManualScore,
                    "普通考试开始前允许直接输入最终总分并跳过小游戏；高考不受影响。");
                LoveTopicLimit = config.Bind(
                    "优化",
                    "情侣话题每回合次数",
                    loveTopicLimit,
                    "支持每回合开展多次情侣话题。默认：1。");
                RelationEffects = Bind(
                    config,
                    "效果",
                    "20关系效果修复与扩展",
                    relationEffects,
                    "针对 20 类关系效果进行修复与扩展。");
                RelationFocusCount = Bind(
                    config,
                    "优化",
                    "关注人数统计优化",
                    relationFocusCount,
                    "优化关注人数统计机制。");
                RoleAvailability = Bind(
                    config,
                    "机制",
                    "控制角色在列表显示",
                    roleAvailability,
                    "控制某角色何时在社交列表显示及是否参加考试（参考慈/谢，本插件使用RoleAvailabilityCfg.json）。");

                config.Save();
                RewriteSectionOrder(config.ConfigFilePath);
            }
            finally
            {
                config.SaveOnConfigSet = saveOnConfigSet;
            }
        }

        private static void ResetConfigFile(ConfigFile config)
        {
            config.Clear();

            try
            {
                string directory = Path.GetDirectoryName(config.ConfigFilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(
                    config.ConfigFilePath,
                    string.Empty,
                    new UTF8Encoding(false));
                config.Reload();
            }
            catch (Exception exception)
            {
                PatchLog.Warning(
                    "配置模块-清理旧配置项目失败；新版项目仍会写入，但旧项目可能暂时保留：" +
                    $"path={config.ConfigFilePath}, reason={ModuleHost.GetReason(exception)}");
            }
        }

        private static void RewriteSectionOrder(string path)
        {
            try
            {
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                int firstSection = Array.FindIndex(lines, line =>
                    line.StartsWith("[", StringComparison.Ordinal) &&
                    line.EndsWith("]", StringComparison.Ordinal));
                if (firstSection < 0)
                    return;

                var header = lines.Take(firstSection).ToList();
                var sections = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                var discoveredOrder = new List<string>();
                for (int index = firstSection; index < lines.Length;)
                {
                    string sectionName = lines[index].Substring(1, lines[index].Length - 2);
                    int next = index + 1;
                    while (next < lines.Length &&
                           !(lines[next].StartsWith("[", StringComparison.Ordinal) &&
                             lines[next].EndsWith("]", StringComparison.Ordinal)))
                    {
                        next++;
                    }

                    sections[sectionName] = lines.Skip(index).Take(next - index).ToList();
                    discoveredOrder.Add(sectionName);
                    index = next;
                }

                string[] desiredOrder = { "优化", "屏幕特效", "效果", "机制", "行动指令" };
                var output = new List<string>(lines.Length);
                output.AddRange(header);
                foreach (string sectionName in desiredOrder.Concat(discoveredOrder).Distinct())
                {
                    if (sections.TryGetValue(sectionName, out List<string> block))
                        output.AddRange(block);
                }

                File.WriteAllText(path, string.Join(Environment.NewLine, output) + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch (Exception exception)
            {
                PatchLog.Warning(
                    "配置模块-按目标分类顺序整理配置文件失败：" +
                    $"path={path}, reason={ModuleHost.GetReason(exception)}");
            }
        }

        private static bool ReadMigratedValue(
            ConfigFile config,
            string section,
            string key,
            string legacySection,
            string legacyKey,
            bool legacyDefault = true)
        {
            bool legacyValue = ReadRawValue(config, legacySection, legacyKey, legacyDefault);
            if (section == legacySection && key == legacyKey)
            {
                return legacyValue;
            }

            return ReadRawValue(config, section, key, legacyValue);
        }

        private static bool ReadRawValue(
            ConfigFile config,
            string section,
            string key,
            bool defaultValue)
        {
            return config.Bind(
                section,
                key,
                defaultValue,
                "旧版配置迁移占位项；保存时会自动移除。").Value;
        }

        private static string ReadRawString(
            ConfigFile config,
            string section,
            string key,
            string defaultValue)
        {
            return config.Bind(
                section,
                key,
                defaultValue,
                "旧版配置迁移占位项；保存时会自动移除。").Value;
        }

        private static ConfigEntry<bool> Bind(
            ConfigFile config,
            string section,
            string key,
            bool value,
            string description)
        {
            ConfigEntry<bool> entry = config.Bind(
                section,
                key,
                true,
                description + " 默认：True。");
            entry.Value = value;
            return entry;
        }
    }
}
