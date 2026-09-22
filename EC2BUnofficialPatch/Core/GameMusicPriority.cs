using System;
using System.Collections.Generic;
using Sdk;
using UnityEngine;

namespace EC2BUnofficialPatch.Core
{
    // Active features share ownership of the original music channel.
    internal static class GameMusicPriority
    {
        private static bool _betterAudio;
        private static bool _video;
        private static readonly HashSet<object> LyricsOwners = new HashSet<object>();
        private static AudioSource _pausedSource;
        private static AudioClip _pausedClip;
        private static bool HasOwner => _betterAudio || _video || LyricsOwners.Count != 0;

        internal static void SetBetterAudio(bool active)
        {
            _betterAudio = active;
            Update();
        }

        internal static void SetVideo(bool active)
        {
            _video = active;
            Update();
        }

        internal static void SetLyrics(object owner, bool active)
        {
            if (owner == null) return;
            if (active) LyricsOwners.Add(owner);
            else LyricsOwners.Remove(owner);
            Update();
        }

        internal static void Update()
        {
            if (!HasOwner && _pausedSource == null)
                return;
            try
            {
                AudioSource source = AudioMgr.Ins?.GetChannel(1)?.source;
                if (HasOwner)
                {
                    if (source != null && source.isPlaying)
                    {
                        // A new original track may start while a lease is held.
                        AudioMgrEx.PauseMusic();
                        _pausedSource = source;
                        _pausedClip = source.clip;
                    }
                    return;
                }

                if (source == null)
                {
                    if (_pausedSource == null) _pausedClip = null;
                    return;
                }
                if (_pausedSource == source && _pausedSource.clip == _pausedClip)
                    AudioMgrEx.UnPauseMusic();
                _pausedSource = null;
                _pausedClip = null;
            }
            catch (Exception exception)
            {
                PatchLog.Warning("原版音乐通道优先级切换失败：" + ModuleHost.GetReason(exception));
            }
        }
    }
}
