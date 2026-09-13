using System;
using System.Collections.Generic;
using System.Reflection;
using Config;
using DG.Tweening;
using HarmonyLib;
using Sdk;
using UnityEngine;

namespace EC2BUnofficialPatch.Core
{
    internal static class TalkViewAccessor
    {
        private static readonly Dictionary<Type, Members> Cache = new Dictionary<Type, Members>();

        internal static void Validate(Type talkViewType)
        {
            GetMembers(talkViewType);
        }

        internal static List<float> GetScreenEffect(object talkView)
        {
            Members members = GetMembers(talkView.GetType());
            TalkCfg cfg = members.Cfg.GetValue(talkView) as TalkCfg;
            return cfg?.screenEffect;
        }

        internal static GameObject GetCurrentBackground(object talkView)
        {
            Members members = GetMembers(talkView.GetType());
            UISprite[] backgrounds = members.Backgrounds.GetValue(talkView) as UISprite[];
            int index = (int)members.CurrentBackgroundIndex.GetValue(talkView);
            if (backgrounds == null || index < 0 || index >= backgrounds.Length || backgrounds[index] == null)
            {
                return null;
            }

            return backgrounds[index].gameObject;
        }

        internal static int GetLayerOrder(object talkView)
        {
            return (int)GetMembers(talkView.GetType()).LayerOrder.GetValue(talkView);
        }

        internal static void KillWaitSequence(object talkView)
        {
            FieldInfo field = GetMembers(talkView.GetType()).TopWaitSequence;
            Sequence sequence = field.GetValue(talkView) as Sequence;
            if (sequence == null)
            {
                return;
            }

            sequence.Kill(false);
            field.SetValue(talkView, null);
        }

        private static Members GetMembers(Type type)
        {
            if (Cache.TryGetValue(type, out Members members))
            {
                return members;
            }

            members = new Members(
                RequireField(type, "cfg"),
                RequireField(type, "bgs"),
                RequireField(type, "curBgIdx"),
                RequireField(type, "layerOrder"),
                RequireField(type, "topWaitSeq"));
            Cache.Add(type, members);
            return members;
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            return AccessTools.Field(type, name)
                ?? throw new MissingFieldException(type.FullName, name);
        }

        private sealed class Members
        {
            internal Members(
                FieldInfo cfg,
                FieldInfo backgrounds,
                FieldInfo currentBackgroundIndex,
                FieldInfo layerOrder,
                FieldInfo topWaitSequence)
            {
                Cfg = cfg;
                Backgrounds = backgrounds;
                CurrentBackgroundIndex = currentBackgroundIndex;
                LayerOrder = layerOrder;
                TopWaitSequence = topWaitSequence;
            }

            internal FieldInfo Cfg { get; }

            internal FieldInfo Backgrounds { get; }

            internal FieldInfo CurrentBackgroundIndex { get; }

            internal FieldInfo LayerOrder { get; }

            internal FieldInfo TopWaitSequence { get; }
        }
    }
}
