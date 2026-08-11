using System.Collections.Generic;
using Sdk;
using TheEntity;

namespace EC2BUnofficialPatch.Services
{
    internal static class RelationFocusCountService
    {
        internal static void SyncSearchFriendCnt(RelationData data)
        {
            if (data == null)
                return;

            HashSet<int> focused = new HashSet<int>();
            Dictionary<int, List<int>> relations = data.GetAllRelationShip();
            if (relations != null)
            {
                foreach (KeyValuePair<int, List<int>> pair in relations)
                {
                    if (pair.Key <= 0 || pair.Value == null)
                        continue;

                    foreach (int personId in pair.Value)
                    {
                        Role role = Singleton<RoleMgr>.Ins.GetRole(personId);
                        if (role == null)
                            continue;

                        // 临时离开仍保留关注名额；永久断绝类状态不占名额。
                        if (role.isLeave && (role.leaveType == -520 || role.leaveType == -525))
                            continue;

                        focused.Add(personId);
                    }
                }
            }

            data.searchFriendCnt = focused.Count;
        }

        internal static void SyncCurrent()
        {
            RoleMgr roleMgr = Singleton<RoleMgr>.Ins;
            if (roleMgr == null)
                return;

            SyncSearchFriendCnt(roleMgr.GetRelationData(false));
        }
    }
}
