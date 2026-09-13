# 架构说明

本版的合并与构建变更见 `../../Docs/MERGE.md`；BA 与 UP 的兼容入口保留在单一 UP 程序集中，在线更新沿用 schema 1 的 `layout=merged` 清单。本文描述当前仓库结构；具体行为以源码与 `Validation_*` 为准。

## 启动与生命周期

```text
Plugin.Awake
  → PluginConfig.Initialize
  → PluginRuntime.Start
    → PluginRuntimeHost.Create（持久宿主）
    → TemplateMaintenance.Ensure
    → PluginServices.Create
    → ModuleHost.Load（按配置加载 Harmony 模块）
    → UpdateService.Start
```

- `Plugin` 只是引导组件。StudentAge 会销毁挂在 `BepInEx_Manager` 上的组件，因此非退出阶段的 `OnDestroy` 不得卸载补丁。
- `PluginRuntimeHost` 是持久生命周期宿主；应用退出才调用 `ModuleHost.Dispose`、释放服务并停止更新检查。
- 每个模块拥有独立 Harmony ID。模块加载失败时会撤销该模块自己的补丁，并记录启动自检失败，不应拖垮无关模块。

## 模块分区

| 区域 | 位置 | 职责 |
|---|---|---|
| 核心 | `Core/` | 配置、生命周期、服务、日志、模板维护、更新。 |
| 机制 | `Features/Mechanics/` | Mod CFG 覆盖、小游戏、地图楼层、LoveDraw 外部资源、角色可用性等。 |
| 屏幕效果 | `Features/ScreenEffects/` | 4006、4016、背景效果、5001 纸条、5002 歌词等。 |
| 效果/行动 | `Features/Effects/`、`Features/ActionCommands/` | 原版 effect 与行动指令扩展。 |
| 优化 | `Features/Optimization/` | CG、静态立绘、地图楼层、JSON 热重载等。 |
| 服务 | `Services/` | 面向多个模块的资源和兼容服务。 |
| 模板 | `ModAuthorTemplate/`、`Examples/` | Mod 作者配置与资源示例。 |

## 原版 CFG 的运行时处理原则

`ModCfgOverrideModule` 必须在游戏读取 Workshop CFG 前加载。它构建运行时 CFG 视图：

- 有增强配置时，替换同名兼容 CFG；
- 原版没有同名配置时，可以注入；
- Workshop 源文件始终不修改。

需要该机制的功能包括 LoveDraw 外部资源、小游戏机制、JSON 热重载、地图子地点兼容及其分层扩展。新增“Mod 可扩展 CFG”时，应优先接入此机制，而不是修改用户已下载的原始配置文件。

## 社交小游戏架构

```text
事件/Option/Talk
  → MiniGameMechanicsPatches
  → MiniGameStageCoordinator（创建会话、记录入口）
  → OriginalMinigameCatalog / OriginalMinigameAdapters
  → 原版 View
  → callback 或 CloseView 观察
  → MiniGameStageCoordinator（单次结算、继续阶段）
```

- `OriginalMinigameCatalog` 是原版 ID、View、启动适配器、结果读取规则与结算模式的唯一目录。
- `MiniGameStageCoordinator` 是唯一允许写入阶段完成状态的协调点；新增玩法不得绕过它直接重复执行阶段结束逻辑。
- `MiniGameStateStore`、`MiniGameStageValidator` 与模板目录负责自定义玩法的数据、校验和作者接口。
- 修改某个原版小游戏时，应同时检查三件事：启动来源（Level/Talk/Option/Evt）、原版结果通道（EndGame/callback/关闭）与失败分支的后续 Talk。

## 地图楼层兼容架构

```text
ModCtrl.MergeCfgsAsync 完成
  → GameModCfgHotReload.RebuildMapFloors
  → MapFloorCompatibilityService.ResolveFloorIds
  → MapSceneView.RefreshFloor 前缀补丁
```

- 合并后重建索引，避免读取旧的或缺失的 `floors` 数据。
- `RefreshFloor` 补丁发生异常时返回原版逻辑并记录错误；这只是故障回退，不意味着不完整的 `MapCfg` 可以被容忍。
- “多 Mod 合并兼容”和“子地点下套子地点”是两个独立配置项，后者默认关闭。

## 地图静态 Mod 角色服装

```text
MapRoleView.Refresh 调用期间
  → TalkRoleItem.SetData(UICell, ...) 前缀
  → 严格确认当前角色使用 Mods 静态立绘
  → 普通地图读取 Role.ClothId / 学校地图保持 1
  → 校验对应 ModFaceCfg 图片文件
  → 缺失时回退 0 号常服
```

- 补丁使用线程内调用范围约束，仅改变 `MapRoleView.Refresh` 发起的立绘初始化。
- 原版角色、Live2D、`DetailSocialView`、Talk 表情和剧情 3006 不进入该适配路径。

## 游戏内调试控制台

- `AutoOpenGameConsoleModule` 仅在 `[Debug] AutoOpenGameConsole = true` 时加载。
- 补丁位于 `DebugView.Start` 后缀；原版完成隐藏初始化后，启用 `DebugMgr.enableDebug` 并调用原版 `SetConsoleShow(true)`。
- 输入框聚焦、反引号切换、Esc 关闭和输入 ActionMap 切换均继续使用原版实现。

## 地图社交界面外置 Live2D

```text
MapRoleView.Refresh 后缀
  → 确认静态 Mod 角色与当前 Role.ClothId / 学校服装
  → ExternalLive2DRegistry 按 personId + clothId + schoolStage 查找
  → ExternalLive2DPackage 校验 model3.json、moc3、PNG 与可选 physics3.json
  → 游戏自带 CubismModel3Json.ToModel
  → 挂载到 Cell_NewTalkRoleItemUI.l2d_role
  → 失败时保持地图静态服装回退结果
```

- `MapSocialExternalLive2DModule` 是默认开启的独立 Harmony 模块，不进入 Talk、`DetailSocialView`、剧情 3006 或原版 Live2D 加载链。
- PNG 由 Unity 异步纹理通道准备；外置模型使用游戏 `GlobalMaskTexture`。模块绕开会提前注册 Source 的属性 Setter，直接写入字段并 `ForceRevive` 重建 Junction，再由 OnEnable 为新 Junction 分配 MaskTile。同一界面实例会隐藏并复用最近的相同模型，而不是在每次关闭时销毁重建。
- `ExternalLive2DRegistry` 读取插件本地及各 Workshop Mod 的 `EC2BUnofficialPatch/ExternalLive2D/ExternalLive2D.json`；以角色、服装和小学/中学建立索引，精确学段优先于 `all`，同一三元组重复声明时全部禁用。
- `ExternalLive2DPackage` 只允许模型根目录内的相对引用，并使用游戏已加载的 Cubism SDK/Core 创建模型；刷新、关闭或失败时释放实例与运行时纹理。
- `ExternalLive2DInstance` 缓存静态 Sprite 的非透明像素边界，并与当前可见 Drawable 边界做逐模型等高换算，从而继承 `PersonCfg` 的位置/缩放；两组归一化锚点处理不同构图，`ExternalLive2DMotionDriver` 在 Cubism 物理之前写入存在的标准头部、眼球、眨眼、身体和呼吸参数。
- F9 热重载会原子替换注册表；已显示的实例在下一次 `MapRoleView.Refresh` 时按新注册表重建。

## 外部资源边界

### 已具备专用桥接

- LoveDraw：外部图片/视频资源由 `LoveDrawExternalResourceModule` 与索引机制处理。
- 5001 ScreenPaper：图片路径在注册表内检查并回退原版。
- 5002 ScreenLyrics：文本与可选音频由注册表处理，并与 BetterAudio 播放状态协调。

### 尚无通用桥接

- Live2D 动作、表情、Animator Controller 与任意 Addressables prefab；地图社交默认姿态的 model3/moc3 已有专用桥接，但不是通用加载器；
- 需要多精灵图集、AudioClip 或原版资源前缀的通用小游戏资源；
- 任意第三方 UI/动画运行时。

新功能若需要这些资源，必须建立专用加载、生命周期与失败回退路径；不能假设 `Mods/<packageId>/...` 的普通图片加载能力会自动覆盖它们。

## 自动更新架构

```text
启动后延迟检查
  → 读取用户镜像 / 原仓库 Release / Raw 的 schema1、layout=merged 清单
  → 校验 schema、版本、HTTPS、大小、SHA-256
  → 下载 DLL 到 .pending
  → 从主 DLL 提取嵌入更新助手
  → 游戏退出后替换 DLL，失败时从 .backup 回滚
```

- 更新目标由 BepInEx `PluginInfo.Location` 解析，沿用实际插件来源；Workshop 桥接 DLL 也可更新。
- 当前只发布并更新合并版 UP DLL；清单上限 256 KiB，DLL 上限 32 MiB，不执行布局迁移。当前发布流程见 `../../Docs/AUTO_UPDATE.md`；`Docs/AutoUpdate.md` 已同步当前协议。

## 文档维护入口

- 项目状态与当前风险：`Docs/STATUS.md`
- 数据格式、原版行为与兼容边界：`Docs/GAME_DATA_FORMATS.md`
- 模块关系、加载路径与可扩展边界：本文件
- 单个版本的精确改动与构建证据：`Docs/CHANGELOG_*.md`、`Docs/Validation_*.md`


## 1.0.21 合并扩展（含 1.0.24/1.0.25）

ContentRootCatalog、CustomMinigameRegistry 与 MiniGameStageCoordinator 的接手扩展均保留在当前 1.0.21。曾在接手版加入的 UpdateTransaction 已移除，助手恢复上游单文件实现。

1.0.24 音频监控：以弱键 AudioClip 关联路径，不缓存永久实例 ID；日志去重按当前/前一帧使用有上限的可复用集合。1.0.25 在线更新：恢复 schema 1 单文件助手，并以 `layout=merged` 防止错误布局覆盖；这些内容均已并入当前版本。
