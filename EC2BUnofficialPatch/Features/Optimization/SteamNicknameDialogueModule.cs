using System;
using System.Collections.Generic;
using System.Reflection;
using EC2BUnofficialPatch.Core;
using HarmonyLib;
using Sdk.PlatformAPI;

namespace EC2BUnofficialPatch.Features.Optimization
{
    internal sealed class SteamNicknameDialogueModule : IPluginModule
    {
        private static readonly IReadOnlyList<ModuleLogItem> Items = new[]
        {
            new ModuleLogItem("优化", "Steam昵称对话替换")
        };

        public string Key => "optimization.steam-nickname-dialogue";
        public IReadOnlyList<ModuleLogItem> LogItems => Items;

        public void Load(Harmony harmony, PluginServices services)
        {
            MethodInfo target = AccessTools.Method(
                typeof(RecordMgr),
                nameof(RecordMgr.Replace),
                new[] { typeof(string) });
            MethodInfo prefix = AccessTools.Method(
                typeof(SteamNicknameDialoguePatches),
                nameof(SteamNicknameDialoguePatches.ReplacePrefix));
            MethodInfo postfix = AccessTools.Method(
                typeof(SteamNicknameDialoguePatches),
                nameof(SteamNicknameDialoguePatches.ReplacePostfix));
            if (target == null || prefix == null || postfix == null)
                throw new MissingMethodException("Steam 昵称对话替换所需的 RecordMgr.Replace 方法不存在。");

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(prefix),
                postfix: new HarmonyMethod(postfix));
        }
    }

    internal static class SteamNicknameDialoguePatches
    {
        private const string ExactToken = "{1}{2}{3}";
        private static bool _platformFailureLogged;

        internal static void ReplacePrefix(
            ref string __0,
            out SteamNicknameReplacementState __state)
        {
            __state = null;
            if (string.IsNullOrEmpty(__0) ||
                __0.IndexOf(ExactToken, StringComparison.Ordinal) < 0)
            {
                return;
            }

            try
            {
                SteamPlatform steam = Platform.Current as SteamPlatform;
                if (steam == null)
                    return;

                string nickname = steam.GetUserName();
                if (string.IsNullOrEmpty(nickname))
                    return;

                // 避免昵称自身包含 {数字} 时被原版 RecordMgr.Replace 二次解释。
                string placeholder =
                    "\uE000EC2B_STEAM_NAME_" + Guid.NewGuid().ToString("N") + "\uE001";
                __0 = __0.Replace(ExactToken, placeholder);
                __state = new SteamNicknameReplacementState(placeholder, nickname);
            }
            catch (Exception exception)
            {
                if (_platformFailureLogged)
                    return;

                _platformFailureLogged = true;
                PatchLog.Exception(
                    "优化模块-Steam昵称对话替换无法读取当前 Steam 昵称；本次保留原版姓名替换",
                    exception);
            }
        }

        internal static void ReplacePostfix(
            ref string __result,
            SteamNicknameReplacementState __state)
        {
            if (__state == null || string.IsNullOrEmpty(__result))
                return;

            __result = __result.Replace(__state.Placeholder, __state.Nickname);
        }
    }

    internal sealed class SteamNicknameReplacementState
    {
        internal SteamNicknameReplacementState(string placeholder, string nickname)
        {
            Placeholder = placeholder;
            Nickname = nickname;
        }

        internal string Placeholder { get; }
        internal string Nickname { get; }
    }
}
