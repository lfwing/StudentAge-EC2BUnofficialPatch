# 外部小游戏接入契约 1.0.23

接口命名空间保持 EC2BUnofficialPatch.Features.Mechanics.Minigames，程序集身份仍为 EC2BUnofficialPatch, Version=1.0.9.0。外部类实现 ICustomMinigame.Open(CustomMinigameContext)，提供公开无参构造。

注册 JSON：

```json
{"minigames":[{"id":9102,"type":"external","dll":"StudentAge.CampusMinigames.dll","class":"实际入口全名","parameters":{"deferStart":"true"}}]}
```

示例中的 DLL/class 必须替换成实际产物。可位于 Mod 的 EC2BUnofficialPatch 目录，也可位于 BepInEx/plugins 下已安装的插件目录；dll 相对 JSON 文件解析。UP 不自动把任意本地 Cfgs 文件写入原版配置：小游戏插件负责只补缺失元数据，或由原版 Mod 提供 MinigameCfg/MinigameActionCfg/PersonGrowCfg。

阶段 ID 是逻辑小游戏 ID*100+阶段，NPC 的 PersonGrowCfg.minigame 选择玩法。NPC 存档使用独立键，两个 NPC 玩同一种游戏也分别推进。游戏只读取 Context 的 NpcId、GameId、ActionCfgId 等上下文，不另存阶段。

- 准备页调用 Begin() 确认开局；返回 false 时不开始，可能是会话失效或余额不足。deferStart=true 的 external 后备启动在 Begin 扣费。普通旧注册保留先扣费的兼容路径；旧 Complete 隐式调用 Begin。
- Cancel() 关闭本次会话且不发奖励、不计胜负；未开始时回滚已扣消耗和尝试次数，已开始时保留消耗。重入/重复调用无效。
- 结束后释放界面/输入，再 Complete(win, selectId) 一次。不要再调用 EndGame、End、TalkFinish 或自行发放阶段奖励。
- IsActive=false 表示不能再 Begin/Complete。Invalidated 在 UP 撤销上下文时通知，监听只负责释放 UI；无须再次取消。准备/进行中第二次 SocialGame 被拒绝。
- 玩家对象变化或本会话经历运行状态后返回主菜单使上下文失效；不向另一份存档退款。新存档加载回调内创建的会话不因尚处 Start 状态被误清。
- 以上生命周期调用必须在 Unity 主线程执行；电脑计算可以后台进行，返回主线程后再次检查 IsActive。
- Option/Talk 内嵌的失败若没有 fail，会继续 success 槽中的原流程回调；此回调不等于胜利，阶段结算仍使用实际 win=false。

## 普通剧情中打开（Talk/Option）

external/dialogue 自定义小游戏也可以在任意剧情事件里由 TalkCfg 或 OptionCfg 的 `miniGame` 字段打开，写法与原版小游戏一致：`miniGame: [自定义ID, 参数1, 参数2, ...]`。成功走 nextTalk / talkId，失败走 nextTalk2 / talkId2，回调语义与内嵌到社交阶段时相同。

- 这种启动没有 NPC 社交阶段：不扣消耗、不回写 MiniGameSubData、不发放阶段奖励。Context 的 `NpcId`、`ActionCfgId` 为 0，`LaunchFrom` 为 Talk/Option，`LaunchSourceId` 为对应的 talkId/optionId，`LaunchParameters` 为 `miniGame[1...]`。
- 小游戏应把关卡/对手等信息放在 `miniGame[1...]` 里自行解释（例如 `[9102, 3]` 表示五子棋第 3 关），并在没有参数时给出默认关卡。
- 同一时间只允许一个剧情小游戏；NPC 社交阶段进行中时，只有该阶段 startTalk 对话图内的 Talk/Option 才会被分发，其它位置仍会被拒绝并走失败分支。
- 读档或回到主菜单会使剧情小游戏上下文失效（Invalidated），不再触发剧情回调。
- 纯对话（dialogue）类型在剧情里直接按成功继续。
