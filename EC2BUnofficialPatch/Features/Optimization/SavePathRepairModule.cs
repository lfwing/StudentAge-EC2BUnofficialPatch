using System;
using System.IO;
using EC2BUnofficialPatch.Core;
using Sdk.PlatformAPI;
using UnityEngine;

namespace EC2BUnofficialPatch.Features.Optimization
{
    /// <summary>
    /// 修复原版存档目录被过早固定的问题。
    ///
    /// 原版 PathDefine 是静态类，第一次被访问时用 Platform.Current.GetUserId() 拼出
    /// persistentDataPath/Saves/&lt;SteamID&gt;/ 并永久固定。BepInEx 插件在 Awake 阶段（SteamManager
    /// 初始化之前）只要间接碰到 PathDefine，GetUserId 就返回 "user"，整局游戏都会读写 Saves/user/：
    /// 玩家看到的是存档全部不可见、已启用 Mod 列表（_mod 文件也在这个目录里）清空，
    /// Player.log 里出现「Load Error: 存档不存在 …\Saves\user\_global」。
    ///
    /// 这里在 Steam 就绪后核对一次，发现固定成了 user 目录就改回 SteamID 目录。
    /// 非 Steam 平台 GetUserId 本来就是 user，不做任何事。
    /// </summary>
    internal static class SavePathRepairModule
    {
        private static bool _done;

        internal static void Tick()
        {
            if (_done) return;
            try
            {
                if (!SteamManager.Initialized) return;
                string userId = Platform.Current.GetUserId();
                if (string.IsNullOrEmpty(userId) || userId == "user") return;
                _done = true;

                string root = Application.persistentDataPath;
                string expected = Path.Combine(root, "Saves", userId);
                string current = PathDefine.SAVE_PATH ?? string.Empty;
                if (string.Equals(Normalize(current), Normalize(expected), StringComparison.OrdinalIgnoreCase)) return;

                PatchLog.Warning(
                    "优化模块-存档目录在 Steam 初始化前被固定为 " + current + "，已改回 " + expected +
                    "。若本次已在错误目录存过档，文件仍在 " + current);
                PathDefine.SAVE_PATH = expected;
                PathDefine.TEST_SAVE_PATH = Path.Combine(root, "Saves_Test", userId);
                PathDefine.IMG_PATH = Path.Combine(expected, "Images");
                PathDefine.MUSIC_PATH = Path.Combine(expected, "Musics");
                Directory.CreateDirectory(expected);
            }
            catch (Exception e)
            {
                _done = true;
                PatchLog.Warning("优化模块-检查存档目录失败：" + e.Message);
            }
        }

        private static string Normalize(string path)
        {
            try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch { return path; }
        }
    }
}
