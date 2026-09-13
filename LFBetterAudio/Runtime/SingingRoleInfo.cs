using System.Collections.Generic;
using Config;
using LFBetterAudio.Preview;
using LFBetterAudio.Timeline;
using Sdk;
using TheEntity;

namespace LFBetterAudio.Runtime
{
    internal sealed class SingingRoleInfo
    {
        internal int Slot { get; set; }
        internal int RoleId { get; set; }
        internal string Name { get; set; }
        internal GenderDefine Gender { get; set; }

        internal int InternalColorMode
        {
            get
            {
                if (Gender == GenderDefine.Male)
                {
                    return TimelineColorPalette.MaleInternalColorId;
                }
                if (Gender == GenderDefine.Female)
                {
                    return TimelineColorPalette.FemaleInternalColorId;
                }
                return TimelineColorPalette.ChoirInternalColorId;
            }
        }
    }

    internal static class SingingRoleResolver
    {
        private const string DefaultPlayerName = "白雨";
        private const string ChoirName = "合唱";

        internal static Dictionary<int, SingingRoleInfo> Resolve(
            IReadOnlyList<int> roleIds,
            TalkChannel channel,
            Dictionary<int, PersonCfg> previewPersonCfgMap,
            GenderDefine previewGender,
            ICollection<string> issues = null)
        {
            var result = new Dictionary<int, SingingRoleInfo>();
            if (roleIds == null)
            {
                return result;
            }

            for (int i = 0; i < roleIds.Count; i++)
            {
                int slot = i + 1;
                result[slot] = ResolveOne(
                    slot,
                    roleIds[i],
                    channel,
                    previewPersonCfgMap,
                    previewGender,
                    issues);
            }

            return result;
        }

        internal static SingingRoleInfo Choir(int slot)
        {
            return new SingingRoleInfo
            {
                Slot = slot,
                RoleId = -1,
                Name = ChoirName,
                Gender = GenderDefine.Unknown
            };
        }

        private static SingingRoleInfo ResolveOne(
            int slot,
            int roleId,
            TalkChannel channel,
            Dictionary<int, PersonCfg> previewPersonCfgMap,
            GenderDefine previewGender,
            ICollection<string> issues)
        {
            if (roleId == 0)
            {
                return ResolvePlayer(slot, channel, previewGender);
            }

            Dictionary<int, PersonCfg> map =
                channel == TalkChannel.Preview && previewPersonCfgMap != null
                    ? previewPersonCfgMap
                    : Cfg.PersonCfgMap;

            PersonCfg cfg = null;
            if (map != null)
            {
                map.TryGetValue(roleId, out cfg);
            }
            if (cfg == null && Cfg.PersonCfgMap != null)
            {
                Cfg.PersonCfgMap.TryGetValue(roleId, out cfg);
            }

            if (cfg == null)
            {
                issues?.Add(
                    $"唱歌角色 id{slot} 对应的 roleId={roleId} 不存在，已按合唱处理。");
                return Choir(slot);
            }

            string name = cfg.name;
            try
            {
                string resolvedName = RoleMgr.GetRoleName(roleId, PersonNameDefine.Full, map);
                if (!string.IsNullOrWhiteSpace(resolvedName))
                {
                    name = resolvedName;
                }
            }
            catch
            {
                // 编辑器自定义人物表可能不完整，直接使用 PersonCfg.name。
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = ChoirName;
            }

            GenderDefine gender = cfg.gender == (int)GenderDefine.Male
                ? GenderDefine.Male
                : cfg.gender == (int)GenderDefine.Female
                    ? GenderDefine.Female
                    : GenderDefine.Unknown;

            return new SingingRoleInfo
            {
                Slot = slot,
                RoleId = roleId,
                Name = name,
                Gender = gender
            };
        }

        private static SingingRoleInfo ResolvePlayer(
            int slot,
            TalkChannel channel,
            GenderDefine previewGender)
        {
            string name = DefaultPlayerName;
            GenderDefine gender = channel == TalkChannel.Preview
                ? previewGender
                : GenderDefine.Unknown;

            try
            {
                Role mainRole = Singleton<RoleMgr>.Ins.GetRole();
                if (mainRole != null)
                {
                    if (!string.IsNullOrWhiteSpace(mainRole.Name))
                    {
                        name = mainRole.Name;
                    }
                    gender = mainRole.Sex;
                }
                else if (gender == GenderDefine.Unknown)
                {
                    gender = Singleton<RoleMgr>.Ins.IsMale()
                        ? GenderDefine.Male
                        : GenderDefine.Female;
                }
            }
            catch
            {
                // 编辑器可能没有载入存档；保留预览性别和默认名字。
            }

            return new SingingRoleInfo
            {
                Slot = slot,
                RoleId = 0,
                Name = name,
                Gender = gender
            };
        }
    }
}
