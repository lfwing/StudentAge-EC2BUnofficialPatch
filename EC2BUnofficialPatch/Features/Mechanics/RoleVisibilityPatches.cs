using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Config;
using EC2BUnofficialPatch.Core;
using EC2BUnofficialPatch.Features.Effects;
using Effect;
using HarmonyLib;
using Increase;
using Newtonsoft.Json;
using Sdk;
using TheEntity;
using View.Common;
using View.TheAction;

namespace EC2BUnofficialPatch.Features.Mechanics
{
    // Store imperative decisions in the existing save's native increaser union.
    // No UID belongs to the executing event: these are saved decisions, not buffs.
    internal static class RoleVisibilityState
    {
        internal const RoleIncType StateType = (RoleIncType)195057;
        private const int Marker = -1955418;
        private sealed class Cached
        {
            internal string Json;
            internal Dictionary<string, bool> Flags;
        }
        private static readonly ConditionalWeakTable<IncreaserOther, Cached> Cache = new ConditionalWeakTable<IncreaserOther, Cached>();
        private static IncreaserOther Node(bool create)
        {
            var role = Singleton<RoleMgr>.Ins.GetRole();
            if (role == null) return null;
            var node = role.IncCtrl.GetModel(StateType)?.Get(0) as IncreaserOther;
            if (node == null && create)
            {
                node = new IncreaserOther(null) { otherAttrId = Marker, toRoleId = 0 };
                role.IncCtrl.Add(StateType, 0, node);
            }
            return node;
        }
        private static Cached Read(IncreaserOther node)
        {
            var cache = Cache.GetValue(node, _ => new Cached());
            if (cache.Flags == null || cache.Json != node.format)
            {
                cache.Json = node.format;
                cache.Flags = string.IsNullOrEmpty(node.format) ? new Dictionary<string, bool>() :
                    JsonConvert.DeserializeObject<Dictionary<string, bool>>(node.format) ?? new Dictionary<string, bool>();
            }
            return cache;
        }
        internal static bool? Get(string key)
        {
            var node = Node(false);
            return node != null && Read(node).Flags.TryGetValue(key, out bool value) ? (bool?)value : null;
        }
        internal static void SetMany(Dictionary<string, bool> changes)
        {
            if (changes.Count == 0) return;
            var node = Node(true);
            if (node == null) return;
            var state = Read(node);
            foreach (var pair in changes) state.Flags[pair.Key] = pair.Value;
            node.format = state.Json = JsonConvert.SerializeObject(state.Flags);
        }
        internal static string Social(int id) => "S/" + id;
        internal static string Post(KZoneContentData post) => "P/" + post.id + "/" + post.postTime;
        internal static string Comment(KZoneContentData post, KZoneCommentData comment) =>
            "C/" + post.id + "/" + post.postTime + "/" + comment.id + "/" + comment.postTime;
        internal static string Like(KZoneContentData post, int role) => "L/" + post.id + "/" + post.postTime + "/" + role;
        internal static string Message(int profile, KZoneMsgData msg) =>
            "M/" + profile + "/" + msg.id + "/" + msg.idx + "/" + msg.seasonYear.Item1 + "/" + msg.seasonYear.Item2;
    }

    internal static class RoleVisibilityPatches
    {
        internal static void Install(Harmony harmony)
        {
            var p = typeof(RoleVisibilityPatches);
            EffectExtensionData.Patch(harmony, typeof(CommonEvtMgr), "GenEffector", p, prefix: nameof(Factory),
                arguments: new[] { typeof(List<float>), typeof(Effector), typeof(int), typeof(int) });
            EffectExtensionData.Patch(harmony, typeof(EffectorSpecial), "OnRun", p, prefix: nameof(Run));
            EffectExtensionData.Patch(harmony, typeof(EffectorSpecial), "OnToString", p, prefix: nameof(Description));
            foreach (var target in new[] {
                new { Type = typeof(KZonePageHomeView), Method = "Refresh" },
                new { Type = typeof(KZonePageHomeView), Method = "HasMore" },
                new { Type = typeof(KZonePageTalkView), Method = "Refresh" },
                new { Type = typeof(KZonePageBlogView), Method = "SetData" },
                new { Type = typeof(KZonePagePhotoView), Method = "SetData" } })
                EffectExtensionData.Patch(harmony, target.Type, target.Method, p, transpiler: nameof(ProjectPosts));
            EffectExtensionData.Patch(harmony, typeof(KZoneCommon), "OnRenderItem", p, transpiler: nameof(ProjectInteractions),
                arguments: new[] { typeof(GenUI.Common.Cell_KZoneItemUI), typeof(bool) });
            EffectExtensionData.Patch(harmony, typeof(KZoneData), "GetMsgs", p, postfix: nameof(Messages));
            EffectExtensionData.Patch(harmony, typeof(KZoneCommon), "SortKZoneContent", p, postfix: nameof(StableOrder));
            EffectExtensionData.Patch(harmony, typeof(KZonePageTalkView), "SortKZoneContent", p, postfix: nameof(StableOrder));
            EffectExtensionData.Patch(harmony, typeof(KZonePageBlogView), "OnKZoneUpdate", p, prefix: nameof(BlogUpdate));
            EffectExtensionData.Patch(harmony, typeof(KZonePageBlogView), "EnterBlog", p, prefix: nameof(EnterBlog));
            EffectExtensionData.Patch(harmony, typeof(BaseView), "OnUpdateEventListeners", p, postfix: nameof(PhotoListeners));
            EffectExtensionData.Patch(harmony, typeof(KZonePageMsgView), "Refresh", p, prefix: nameof(MessageReply));
        }
        private static bool Matches(List<float> e) => e != null && e.Count >= 2 &&
            ((e[0] == 22 && e[1] == 999) || (e[0] == 23 && e[1] == 3));
        private static bool Valid(List<float> e) => e.Count == 4 && EffectExtensionData.Finite(e[3]) &&
            e[3] >= 0 && e[3] < int.MaxValue && e[3] == Math.Truncate(e[3]) &&
            (e[0] == 22 ? e[2] == 2 || e[2] == 3 : e[2] == 1 || e[2] == -1 || e[2] == 2 || e[2] == -2 || e[2] == 999 || e[2] == -999);
        internal static bool Factory(List<float> _effect, Effector _effector, int _toRoleId, int _fromRoleId, ref Effector __result)
        {
            if (!Matches(_effect)) return true;
            __result = new EffectorSpecial(_effector, _effect) { toRoleId = _toRoleId, fromRoleId = _fromRoleId };
            return false;
        }
        internal static bool Run(EffectorSpecial __instance)
        {
            var e = EffectExtensionData.Get(__instance);
            if (!Matches(e)) return true;
            if (!Valid(e)) { PatchLog.Warning("角色可见性-无效效果参数：" + string.Join(",", e)); return false; }
            int id = (int)e[3];
            if (e[0] == 22)
            {
                if (id <= 0 || Cfg.PersonCfgMap == null || !Cfg.PersonCfgMap.ContainsKey(id))
                    PatchLog.Warning("角色可见性-角色不存在：" + id);
                else
                {
                    RoleVisibilityState.SetMany(new Dictionary<string, bool> { [RoleVisibilityState.Social(id)] = e[2] == 2 });
                    EventMgr.Send(401);
                }
            }
            else
            {
                var kzone = Singleton<RoleMgr>.Ins.GetKZoneData();
                if (kzone != null && ChangeSpace(kzone, (int)e[2], id) > 0) EventMgr.Send(13001);
            }
            return false;
        }
        internal static bool Description(EffectorSpecial __instance, ref string __result)
        {
            var e = EffectExtensionData.Get(__instance);
            if (!Matches(e)) return true;
            if (!Valid(e)) { __result = "无效的角色可见性效果（此项不生效）"; return false; }
            string id = EffectExtensionData.Number(e[3]);
            if (e[0] == 22) __result = e[2] == 2 ? "在社交列表里显示" + id + "关注对象" : "在社交列表里隐藏" + id;
            else if (Math.Abs(e[2]) == 999) __result = "将" + id + "关联的、已发出" + (e[2] > 0 ? "但未显示的空间内容全部显示" : "且显示的空间内容全部隐藏");
            else __result = "将已发出的" + id + (Math.Abs(e[2]) == 1 ? "动态" : "评论") + (e[2] > 0 ? "进行显示" : "进行隐藏");
            return false;
        }

        internal static bool DefaultVisible(int author) => !RoleAvailabilityPatches.HiddenKZone(author);
        internal static bool PostVisible(KZoneContentData post) => post != null &&
            (RoleVisibilityState.Get(RoleVisibilityState.Post(post)) ?? DefaultVisible(post.RoleId));
        internal static bool CommentVisible(KZoneContentData post, int id)
        {
            var visited = new HashSet<int>();
            while (id > 0)
            {
                if (!visited.Add(id) || !post.comments.TryGetValue(id, out var comment) || !Cfg.KZoneCommentCfgMap.TryGetValue(id, out var cfg)) return false;
                int author = cfg.roles != null && cfg.roles.Count > 0 ? cfg.roles[0] : 0;
                if (!(RoleVisibilityState.Get(RoleVisibilityState.Comment(post, comment)) ?? DefaultVisible(author))) return false;
                id = cfg.parent;
            }
            return true;
        }
        internal static Dictionary<int, KZoneContentData> VisiblePosts(KZoneData data) => data.datas == null ? null :
            data.datas.Where(pair => PostVisible(pair.Value)).ToDictionary(pair => pair.Key, pair => pair.Value);
        internal static Dictionary<int, KZoneCommentData> VisibleComments(KZoneContentData post) =>
            post.comments.Where(pair => CommentVisible(post, pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
        internal static List<int> VisibleLikes(KZoneContentData post) => post.thumbs.Where(role =>
            RoleVisibilityState.Get(RoleVisibilityState.Like(post, role)) ?? DefaultVisible(role)).ToList();
        internal static int VisibleAnonymousLikeCount(KZoneContentData post) =>
            Math.Max(0, post.customThumbCnt - (post.thumbs.Count - VisibleLikes(post).Count));
        internal static void Messages(int _kzoneRoleId, ref List<KZoneMsgData> __result)
        {
            if (__result == null) return;
            var all = __result;
            var visible = new HashSet<KZoneMsgData>(all.Where(msg =>
                RoleVisibilityState.Get(RoleVisibilityState.Message(_kzoneRoleId, msg)) ?? DefaultVisible(msg.RoleId)));
            // Replies retain their own decision, but cannot expose a hidden parent.
            bool changed;
            do {
                changed = false;
                foreach (var msg in visible.ToArray())
                    if (msg.parentId > 0 && !visible.Any(parent => parent.id == msg.parentId))
                        changed |= visible.Remove(msg);
            } while (changed);
            __result = all.Where(visible.Contains).ToList();
        }
        internal static int ChangeSpace(KZoneData data, int mode, int id)
        {
            bool show = mode > 0;
            var changes = new Dictionary<string, bool>();
            bool bulk = Math.Abs(mode) == 999;
            foreach (var post in data.datas?.Values ?? Enumerable.Empty<KZoneContentData>())
            {
                bool postVisible = PostVisible(post);
                if ((Math.Abs(mode) == 1 && post.id == id) || (bulk && post.RoleId == id && postVisible != show))
                    changes[RoleVisibilityState.Post(post)] = show;
                if (Math.Abs(mode) == 2 || bulk)
                    foreach (var comment in post.comments.Values)
                        if ((!bulk && comment.id == id) || (bulk && Cfg.KZoneCommentCfgMap.TryGetValue(comment.id, out var cfg) && cfg.roles != null && cfg.roles.Count > 0 && cfg.roles[0] == id &&
                            (postVisible && CommentVisible(post, comment.id)) != show))
                            changes[RoleVisibilityState.Comment(post, comment)] = show;
                if (bulk && post.thumbs.Contains(id) && (postVisible && (RoleVisibilityState.Get(RoleVisibilityState.Like(post, id)) ?? DefaultVisible(id))) != show)
                    changes[RoleVisibilityState.Like(post, id)] = show;
            }
            if (bulk && data.msgs != null)
                foreach (var board in data.msgs)
                {
                    var visible = board.Value;
                    Messages(board.Key, ref visible);
                    foreach (var msg in board.Value)
                        if (msg.RoleId == id && visible.Contains(msg) != show) changes[RoleVisibilityState.Message(board.Key, msg)] = show;
                }
            RoleVisibilityState.SetMany(changes);
            if (changes.Count == 0) PatchLog.Info("角色可见性-没有匹配的已发布空间内容：mode=" + mode + ", id=" + id);
            return changes.Count;
        }
        internal static IEnumerable<CodeInstruction> ProjectPosts(IEnumerable<CodeInstruction> instructions) =>
            ReplaceFields(instructions, new Dictionary<string, string> { ["datas"] = nameof(VisiblePosts) }, typeof(KZoneData));
        internal static IEnumerable<CodeInstruction> ProjectInteractions(IEnumerable<CodeInstruction> instructions) =>
            ReplaceFields(instructions, new Dictionary<string, string> {
                ["comments"] = nameof(VisibleComments), ["thumbs"] = nameof(VisibleLikes), ["customThumbCnt"] = nameof(VisibleAnonymousLikeCount) }, typeof(KZoneContentData));
        private static IEnumerable<CodeInstruction> ReplaceFields(IEnumerable<CodeInstruction> instructions, Dictionary<string, string> fields, Type owner)
        {
            foreach (var code in instructions)
            {
                foreach (var pair in fields)
                    if (code.opcode == OpCodes.Ldfld && Equals(code.operand, AccessTools.Field(owner, pair.Key)))
                    {
                        code.opcode = OpCodes.Call;
                        code.operand = AccessTools.Method(typeof(RoleVisibilityPatches), pair.Value);
                        break;
                    }
                yield return code;
            }
        }
        internal static void StableOrder(KZoneContentData a, KZoneContentData b, ref int __result)
        {
            if (__result == 0) __result = a.id.CompareTo(b.id);
        }
        internal static bool BlogUpdate(KZonePageBlogView __instance, KZoneContentData ___blog, KZoneProfileData ___profile, int ___themeId)
        {
            if (___profile == null) return false;
            if (___blog != null && PostVisible(___blog) && __instance.root_blog_item.gameObject.activeSelf) return true;
            __instance.SetData(___profile.id, ___themeId);
            return false;
        }
        internal static bool EnterBlog(KZoneContentData _data) => PostVisible(_data);

        private sealed class PhotoListener
        {
            internal KZonePagePhotoView View;
            internal void Refresh()
            {
                var profile = AccessTools.FieldRefAccess<KZonePagePhotoView, KZoneProfileData>("profile")(View);
                if (profile != null)
                    View.SetData(profile.id, AccessTools.FieldRefAccess<KZonePagePhotoView, int>("themeId")(View));
            }
        }
        private static readonly ConditionalWeakTable<KZonePagePhotoView, PhotoListener> PhotoCallbacks =
            new ConditionalWeakTable<KZonePagePhotoView, PhotoListener>();
        internal static void PhotoListeners(BaseView __instance)
        {
            if (__instance is KZonePagePhotoView photo)
                photo.UpdateListener(13001, PhotoCallbacks.GetValue(photo, view => new PhotoListener { View = view }).Refresh);
        }
        internal static void MessageReply(KZonePageMsgView __instance, KZoneData ___kzone, int ___kzoneRoleId, ref KZoneMsgData ___replyData)
        {
            if (___replyData == null || ___kzone == null || ___kzone.GetMsgs(___kzoneRoleId)?.Contains(___replyData) == true) return;
            ___replyData = null;
            __instance.group_reply.gameObject.SetActive(false);
        }
    }
}
