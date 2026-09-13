using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Config;
using EC2BUnofficialPatch.Core;
using GenUI.Common;
using HarmonyLib;
using Sdk;
using TheEntity;
using View.Common;
using View.Main;

namespace EC2BUnofficialPatch.Features.Mechanics
{
    internal sealed class MapRoleStaticClothModule : IPluginModule
    {
        private static readonly IReadOnlyList<ModuleLogItem> Items = new[]
        {
            new ModuleLogItem("机制", "地图静态Mod角色随机服装显示")
        };

        public string Key => "mechanics.map-role-static-cloth";
        public IReadOnlyList<ModuleLogItem> LogItems => Items;

        public void Load(Harmony harmony, PluginServices services)
        {
            MethodInfo refresh = AccessTools.Method(typeof(MapRoleView), nameof(MapRoleView.Refresh), Type.EmptyTypes)
                ?? throw new MissingMethodException(typeof(MapRoleView).FullName, nameof(MapRoleView.Refresh));
            MethodInfo setData = AccessTools.Method(
                typeof(TalkRoleItem),
                nameof(TalkRoleItem.SetData),
                new[]
                {
                    typeof(UICell), typeof(int), typeof(int), typeof(float), typeof(int), typeof(bool),
                    typeof(int), typeof(int), typeof(int), typeof(int), typeof(GenderDefine),
                    typeof(L2DLoadType), typeof(Dictionary<int, PersonCfg>)
                }) ?? throw new MissingMethodException(typeof(TalkRoleItem).FullName, nameof(TalkRoleItem.SetData));

            harmony.Patch(
                refresh,
                prefix: new HarmonyMethod(typeof(MapRoleStaticClothPatches), nameof(MapRoleStaticClothPatches.RefreshPrefix)),
                finalizer: new HarmonyMethod(typeof(MapRoleStaticClothPatches), nameof(MapRoleStaticClothPatches.RefreshFinalizer)));
            harmony.Patch(
                setData,
                prefix: new HarmonyMethod(typeof(MapRoleStaticClothPatches), nameof(MapRoleStaticClothPatches.SetDataPrefix)));

            PatchLog.Registration("机制模块-地图静态Mod角色随机服装显示已启用：仅作用于 MapRoleView");
        }
    }

    internal static class MapRoleStaticClothPatches
    {
        [ThreadStatic]
        private static int _mapRoleRefreshDepth;

        public static void RefreshPrefix()
        {
            _mapRoleRefreshDepth++;
        }

        public static Exception RefreshFinalizer(Exception __exception)
        {
            if (_mapRoleRefreshDepth > 0)
                _mapRoleRefreshDepth--;
            return __exception;
        }

        public static void SetDataPrefix(UICell _cell, ref int _cloth, int _gradeState)
        {
            if (_mapRoleRefreshDepth <= 0 || _cell == null || !(_cell.data is int roleId) || roleId <= 0)
                return;

            try
            {
                Role npc = Singleton<RoleMgr>.Ins.GetRole(roleId);
                if (npc == null || !Cfg.PersonCfgMap.TryGetValue(roleId, out PersonCfg personCfg))
                    return;

                int gradeState = _gradeState == -1
                    ? Singleton<RoleMgr>.Ins.GetRole().GradeState
                    : _gradeState;
                if (!IsStaticModRole(personCfg, gradeState))
                    return;

                bool schoolCloth = _cloth == 1;
                int requestedCloth = GetRequestedCloth(npc, schoolCloth);
                int resolvedCloth = ResolveStaticCloth(personCfg, requestedCloth, gradeState);

                _cloth = resolvedCloth;
                PatchLog.Debug(
                    "机制模块-地图静态Mod角色服装解析：" +
                    $"role={roleId}, school={schoolCloth}, roleCloth={npc.ClothId}, " +
                    $"requested={requestedCloth}, resolved={resolvedCloth}");
            }
            catch (Exception exception)
            {
                _cloth = 0;
                PatchLog.Warning(
                    "机制模块-地图静态Mod角色服装解析失败，已回退0号常服：" +
                    ModuleHost.GetReason(exception));
            }
        }

        internal static bool IsStaticModRole(PersonCfg personCfg, int gradeState)
        {
            if (personCfg == null || personCfg.id <= 0 || !personCfg.IsUseImg(gradeState, 0))
                return false;

            List<string> urls = personCfg.GetRoleUrls(gradeState);
            if (urls == null)
                return false;
            foreach (string url in urls)
            {
                if (!string.IsNullOrWhiteSpace(url) &&
                    url.StartsWith("Mods", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        internal static int GetRequestedCloth(Role npc, bool schoolCloth)
        {
            return schoolCloth ? 1 : Math.Max(0, npc?.ClothId ?? 0);
        }

        internal static int ResolveStaticCloth(PersonCfg personCfg, int requestedCloth, int gradeState)
        {
            return requestedCloth != 0 && !HasUsableModFaceResource(personCfg, requestedCloth, gradeState)
                ? 0
                : requestedCloth;
        }

        internal static bool HasUsableModFaceResource(PersonCfg personCfg, int clothId, int gradeState)
        {
            if (personCfg == null || clothId <= 0 || Cfg.ModFaceCfgMap == null)
                return false;

            string resource = RoleMgr.GetExpressionIcon(personCfg, clothId, 0, gradeState, null);
            if (string.IsNullOrWhiteSpace(resource))
                return false;

            if (resource.StartsWith("Mods", StringComparison.OrdinalIgnoreCase))
            {
                string fullPath = Singleton<ModCtrl>.Ins.GetFullUrl(resource, null);
                return !string.IsNullOrWhiteSpace(fullPath) && File.Exists(fullPath);
            }

            if (Path.IsPathRooted(resource))
                return File.Exists(resource);

            // 非文件型资源地址由原版 UISprite/ResMgr 继续解析，避免把 Addressables 误判为缺失。
            return true;
        }
    }
}
