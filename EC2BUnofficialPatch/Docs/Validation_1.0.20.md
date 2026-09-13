# 1.0.20 验证记录

## 构建与公开接口

- Debug/net472：通过，0 警告、0 错误。
- Release/net472：通过，0 警告、0 错误。
- 文件版本与产品版本为 1.0.20；程序集版本继续保持 1.0.9.0，避免破坏按旧稳定程序集版本引用 UP 的插件。
- Release DLL 对外导出 `EC2BUnofficialPatch.Core.JsonHotReloadApi` 与 `JsonHotReloadResult`。
- 主 DLL 仍只嵌入一个更新助手资源：`EC2BUnofficialPatch.Embedded.Updater.exe`。

## 热重载结构

- F9 由跨场景持久运行时宿主在 Unity 主线程通过游戏启用的 Unity Input System 检测；关闭配置项后不执行。输入后端异常时只记录一次并停止轮询，不会逐帧刷错。
- 原版 `ModCtrl.LoadModCfgs` 的实际启用 Mod 顺序和 CFG 路径会被记录。
- 原版 `MergeCfgsAsync` 合并前捕获纯游戏 CFG 基线；热重载先完整构建新计划，再原位替换各 `CfgMap`，异常时恢复运行时 Map 与 `ModCtrl.cfgMaps`。
- 所有经原版 `ModCtrl.LoadModCfgs` 启用的 Mod 都会纳入，不要求安装 UP/BA 或提供插件专用目录；单文件解析失败时保留该文件旧注册，其他有效文件继续处理。
- 文件内容使用指纹比较；未变化文件不重新反序列化、不逐条输出。发生变化时只记录对应 Mod、CFG 类型以及新增/修改/删除的条目 ID，错误始终输出完整文件路径。
- UP JSON 在切换前统一预检语法并构建新注册表；漫画、情侣画、歌词与纸条资源索引随之更新。
- BetterAudio 1.0.0 源码包可正常构建；适配所依赖的 `DiscoverWorkshopPackages`、`LoadMusicPackages`、`ConfigStore` 和 `ConfigPath` 成员均已核对存在。

## 社交小游戏、地图与文本替换

- 原版小游戏按 Level、内嵌回调和关闭观察三类契约启动及结算；原版 EndGame 与适配器回调不会同时取得结算所有权。
- 钢琴、戳指缝、数独、拼图等独立 Level 玩法在打开前验证各自依赖 CFG；无独立契约的玩法不会再被空参数强制打开。
- `多mod地图子地点兼容` 默认开启，只汇总父地图的多来源 `floors`；未声明的type=2地图按ID/1000兜底，重建失败时恢复原列表。
- `地图子地点扩展` 为独立且默认关闭的开关；仅开启后才解释type=2自身的floors并形成多层父子图，叶子返回直接父层、容器显示自身下一层。
- 临时配置生成验证确认该开关的说明、BepInEx默认值元数据和实际配置值均为false。
- 开关隔离验证通过：关闭扩展时type=2不成为下一层容器；开启扩展后同一配置按分层结构显示。
- 独立验证覆盖原版单层结构、只有“祖先+自身”的错误子列表、包含任意ID下级地图的合法两层结构，三组均通过。
- BetterAudio兼容层在`PluginInfo.Instance`为空时仍可通过已加载程序集的静态发现器和`ConfigStore.LoadAll`重载JSON注册。
- Steam 昵称替换仅保护严格连续的 `{1}{2}{3}`，原版完成其他文本替换后再恢复昵称；Steam 不可用时回退原版结果。

## Release 产物

- `EC2BUnofficialPatch.dll`：347136 bytes
- SHA-256：`4FFB82B78408929F712F747D3282D262CC0260F55E68E971FDBC9A10C6CBAE79`
- `update.json` 的版本、大小、哈希和 1.0.20 下载地址与产物一致。
