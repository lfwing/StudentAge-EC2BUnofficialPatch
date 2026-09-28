# UP 1.0.21 验证范围

- 交付构建与根 EC2BUnofficialPatch.csproj 的 Release 构建均成功。修正了 MSBuild 从测试输出目录误选 Newtonsoft.Json 的引用顺序，明确优先本机游戏引用；现有一个音频控制器未读私有字段警告保留。
- 恢复原 update.json / EC2BUnofficialPatch.dll 名称后重新构建，更新器 26 项定向检查通过：仅接受 merged 清单，拒绝分离/无标记/非法清单，保留镜像→Release→Raw 兼容回退、版本比较、HTTPS/摘要/大小校验、plugins/Workshop 位置替换、备份和损坏拒绝。公开清单仅发布到 Release，仓库根目录的 Raw 清单已移除。助手测试使用临时目录与 pid=0，不代表正式在线更新验收。
- 文件名恢复前的同版本构建已在隔离 CrossOver 游戏启动完成 22 项音频接管检查（本次仅更新入口和附件名，未重跑该音频检查）：UP 版本 1.0.21、DLL 中仅一个 BepInEx 入口、无独立 BA 注册、持久音频控制器、重复初始化、音频补丁归属、1163 播放真实 WAV、5002 暂停/恢复、资源注册表热重载保留播放、旧暂停/恢复/停止指令和退出清理均通过。
- 本轮不重复小游戏、Live2D 或完整周目联调；1.0.24/1.0.25 的构建、更新事务和公开 API 历史证据已合并到当前 1.0.21 记录，但不能替代本版完整验收。未进行原生 Windows UI 或正式发布验收。
- 首次从 1.0.23～1.0.25 测试版切回 1.0.21 需要手动覆盖，不放开自动降级。

## 交付 DLL

本次 Release 构建 EC2BUnofficialPatch.dll：535552 bytes，SHA256 `C8A64FA935BD89C8B0B707ACCCA982D1098DCEC925DCBC40F2EC32F681AE9C39`。插件版本/产品版本 1.0.21，文件版本 1.0.21.0；UP 程序集绑定身份保持 1.0.9.0。构建使用仓库现有 Release 游戏引用；保留一个既有 `BetterAudioController._applicationQuitting` 未使用字段警告，0 个错误。

`release-manifests/update.json` 已同步本次构建的大小和摘要；仓库根清单待配套 DLL 正式发布后更新，没有自动发布 Release。

## 2026-09-28：事件 ID 兼容整合（1.0.22，待合并）

- 单 UP DLL 构建通过，0 错误；保留现有 BetterAudioController 未使用字段警告。插件版本 1.0.22，程序集身份 1.0.9.0，无独立事件插件入口。
- 34 项 ID 逻辑检查和 26 项更新器回归通过。纯逻辑覆盖三表同号、原版覆盖、引用白名单、历史/变量、加载顺序、停用/新增 Mod、浮点精度、映射持久化与保留 ID 冲突拒绝。
- 整合后的 UP 在隔离 CrossOver / 游戏 1.94 中通过 50 项检查：真实 LoadModCfgs/MergeCfgsAsync、UP 增强 LoveDrawCfg 注入、两个同号新增事件自然入队且原版覆盖只有一个、独立条件/次数/变量、原版手动保存和 Game.LoadGame、两组实际对白与原版选项结果；普通 CFG 的 F9 提供者保留映射和历史、重建新增事件索引、保留坏文件上次有效内容，并在别名冲突或索引构建异常时回滚。
- 读档 QA 必须等待 MainView/TopView 过渡完成；初版测试过早调用 Game.LoadGame 导致原版 RoleAttrView 移除尚未注册的监听，已修正驱动时序后通过。配置初始化初版在 config.Clear 后读取新开关导致关闭，也已修正并通过实际启用加载。
- 反序启动的 50 项检查再次通过，事件/对白/选项 ID 与正序结果一致，原版覆盖赢家随原版加载顺序改变。关闭开关的 10 项检查保持原版合并和普通 F9 行为；旧独立 DLL 共存的 11 项检查确认 UP 不重复映射且拒绝普通 CFG 热重载。移除旧 DLL、保留其映射并反序加载后，最终构建再通过 50 项，迁移后的 ID 与初始正序完全一致。
- 每次测试比较正式游戏代码、存档和原版设置命名空间，1,804 项保持不变。管理层存档/PlayerPrefs、原生 PlayerSettings 及 Steam 写入均隔离；测试自动更新关闭。
- 调用的是 F9 注册的普通 CFG 提供者，不宣称模拟实体按键；没有原生 Windows、完整周目、全部工坊模组组合或正式发布验收。任意 DLL 硬编码、自定义协议和 Mod 对另一个 Mod 新增 ID 的有意覆盖仍需专门适配。

可复现源码与操作在 `tools/EventCompatibilityTests/README.md`。游戏程序集、截图、测试存档和本机日志不进入 PR。测试生产 DLL SHA256：`ce8f5ca144614046d9fc569c826ac3d6ebfdf95d06417bdab6d4ed0148d19fba`；游戏 Assembly-CSharp SHA256：`0d004b3a8c7fdd2ac41e4db099293db112d6b24dc2a14086e9c674283f1378ea`。
