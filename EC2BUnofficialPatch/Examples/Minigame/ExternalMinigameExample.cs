using EC2BUnofficialPatch.Features.Mechanics.Minigames;

namespace Example
{
    public sealed class TankBattleMinigame : ICustomMinigame
    {
        public void Open(CustomMinigameContext context)
        {
            // JSON 静态参数：context.Parameters
            // Talk/Option miniGame[1...]：context.LaunchParameters
            // 启动来源：context.LaunchFrom / context.LaunchSourceId
            // 普通剧情里由 TalkCfg/OptionCfg 的 miniGame:[id, 关卡...] 打开时，
            // context.NpcId 与 context.ActionCfgId 为 0，关卡从 LaunchParameters 取；
            // 社交阶段启动时可用 ActionCfgId - GameId*100 作为阶段号。

            // 在此创建/打开自己的 Unity View。
            // View 结束时调用且只调用一次：
            // context.Complete(true);   // 胜利
            // context.Complete(false);  // 失败
        }
    }
}
