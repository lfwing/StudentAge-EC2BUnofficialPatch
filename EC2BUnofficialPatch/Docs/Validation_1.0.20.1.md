# 1.0.20.1 验证记录

## Option 内嵌小游戏结算

- 复核游戏 1.93 的 `NegotiationMiniGameView.CloseView`：Option 成功/失败分支会先执行对应 effect，再尝试以 `success`/`fail` 作为结果 Talk 回调；已打开的 `NewTalkView` 会保留社交阶段原 callback，导致 1.0.20 的回调结算丢失。
- 回调型小游戏现已纳入 CloseView 补丁安装范围；运行时只在 `from == Option` 时启用关闭观察，不改变 Talk/Evt 回调型小游戏的原结算路径。
- Option 内嵌小游戏能读取明确结果时，在原版 CloseView 打开结果 Talk 或调用 success/fail 前回写一次阶段；后续原版回调命中既有 `Settled/SettlementInProgress` 防护，不会再次调用 `MiniGameSubData.End/TalkFinish`。
- Option 失败分支缺少独立 fail callback 时，继续使用原版放在 success 槽中的阶段流程 callback，避免无后续 Talk 时中断。
- Option 没有结果 Talk 时，具体 View 关闭后清理已结算会话；存在结果 Talk 时，保留会话到原阶段 callback 完成。

## 兼容性边界

- `from == Talk` 的回调型小游戏不会启用关闭前接管，仍在 success/fail/result 回调到达时结算。
- 原版 EndGame、回调与关闭观察最终都经过同一会话状态锁；1.0.20 的双倍结算防护保持不变。
- `OpenMiniGamePrefix` 的参数列表、逻辑 ID/实现 ID 映射与临时 MinigameCfg 交换代码未改动，1.0.20 的参数适配保持不变。
- 程序集版本继续保持 `1.0.9.0`。

## 构建与发布产物

- Debug/net472、Release/net472：均通过，0 警告、0 错误。
- 文件版本：`1.0.20.1`。
- `EC2BUnofficialPatch.dll`：347648 bytes。
- SHA-256：`A4F3A56D3CDA4B234C137228C0145A4BFC2BD8F1333C5F0816318884AE795363`。
- `update.json` 的版本、大小、哈希和 1.0.20.1 下载地址与发布 DLL 一致。
- 发布 ZIP 包含主 DLL、Mod 作者模板与 README，共 8 个条目。
