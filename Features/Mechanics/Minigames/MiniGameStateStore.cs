using System;
using System.Collections.Generic;
using System.Reflection;
using Config;
using EC2BUnofficialPatch.Core;
using HarmonyLib;

namespace EC2BUnofficialPatch.Features.Mechanics.Minigames
{
    internal static class MiniGameStateStore
    {
        private static readonly FieldInfo GamesField =
            AccessTools.Field(typeof(MiniGameData), "games");

        internal static MiniGameSubData GetOrCreateForNpc(
            MiniGameData owner,
            int npcId,
            int logicalGameId = 0)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            if (logicalGameId <= 0)
            {
                if (!Cfg.PersonGrowCfgMap.TryGetValue(npcId, out PersonGrowCfg grow))
                {
                    throw new KeyNotFoundException($"PersonGrowCfg 不存在：npc={npcId}");
                }

                logicalGameId = grow.minigame;
            }

            Dictionary<int, MiniGameSubData> games = GetGames(owner);
            int key = ToNpcKey(npcId);
            if (games.TryGetValue(key, out MiniGameSubData state) && state != null)
            {
                if (state.id == logicalGameId)
                {
                    state.npcId = npcId;
                    return state;
                }

                PatchLog.Warning(
                    "机制模块-NPC 的小游戏配置已改变，旧阶段不会套用到新玩法：" +
                    $"npc={npcId}, old={state.id}, new={logicalGameId}");
                state = Create(logicalGameId, npcId);
                games[key] = state;
                return state;
            }

            // 兼容旧存档：原版把社交小游戏放在 games[小游戏ID]。
            if (games.TryGetValue(logicalGameId, out MiniGameSubData legacy) &&
                legacy != null && legacy.npcId == npcId)
            {
                games.Remove(logicalGameId);
                legacy.id = logicalGameId;
                legacy.npcId = npcId;
                games[key] = legacy;
                PatchLog.Info(
                    "机制模块-已迁移原版 NPC 小游戏存档到角色独立键：" +
                    $"npc={npcId}, minigame={logicalGameId}, key={key}, cfg={legacy.cfgId}");
                return legacy;
            }

            state = Create(logicalGameId, npcId);
            games[key] = state;
            return state;
        }

        internal static int GetCurrentCfgId(MiniGameData owner, int npcId, int logicalGameId) =>
            GetOrCreateForNpc(owner, npcId, logicalGameId).cfgId;

        private static Dictionary<int, MiniGameSubData> GetGames(MiniGameData owner)
        {
            if (GamesField == null)
            {
                throw new MissingFieldException(typeof(MiniGameData).FullName, "games");
            }

            Dictionary<int, MiniGameSubData> games =
                GamesField.GetValue(owner) as Dictionary<int, MiniGameSubData>;
            if (games == null)
            {
                games = new Dictionary<int, MiniGameSubData>();
                GamesField.SetValue(owner, games);
            }

            return games;
        }

        private static MiniGameSubData Create(int logicalGameId, int npcId) =>
            new MiniGameSubData
            {
                id = logicalGameId,
                npcId = npcId
            };

        private static int ToNpcKey(int npcId)
        {
            if (npcId <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(npcId),
                    npcId,
                    "社交小游戏 NPC ID 必须为正数。");
            }

            return -npcId;
        }
    }
}
