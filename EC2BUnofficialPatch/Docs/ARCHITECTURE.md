# 架构说明

## 角色可见性、特质计时与控制台历史（2026-10-25）

RoleAvailabilityModule保留社交过滤和考试池保护，增加RoleVisibilityPatches。RoleAvailabilityService的hiddenkzone只提供空间默认值，原cond仍为默认社交与考试准入条件。22/999通过SocialAvailable查询保存的指令决定，然后回退cond；23/3只修改已发布记录的显示决定。原KZoneData.datas/comments/thumbs/msgs不删除、不替换，发布和定时更新路径不挂过滤。

RoleVisibilityState在主角RoleIncType=195057中放一个原生IncreaserOther（marker=-1955418，value=0），format以JSON保存社交与空间决定。不是执行效果的UID子节点，不随特质切换撤销。缓存按节点弱引用关联；读档换节点或format变化即重新读取，F9只替换默认服务。动态键包含原发布时间，评论键含父动态与评论时间，留言键含空间/ID/楼层/季年，防止重新发布后误用旧动态决定。批量遍历当前原始记录，只对可见状态需要改变的匹配作者项写覆盖，不创建未来预约。

主页/HasMore/说说/日志/相册的KZoneData.datas字段读取转为只读投影；OnRenderItem内comments/thumbs及点赞计数读取同样投影。留言GetMsgs后缀返回过滤副本；回复要求父留言可见。评论不显示隐藏祖先的后代。时间排序保留原postTime，同时间戳以ID确定顺序。13001更新触发相关原版视图刷新；原版相册缺少该监听，补在BaseView监听生命周期且只处理KZonePagePhotoView，避免独立订阅泄漏。已隐藏日志详情回到列表，隐藏留言不能继续作为回复目标。

TraitCooldownPatches随NewEffectModule加载，20/5418工厂使用安全EffectorSpecial，原生IncreaserAttr在195056中保存开关并归属原版UID/关系倍率。只覆盖DetailSocialView切换按钮的interactable和ActionDescHelper对应说明，不改traitChangeCnt或解锁关系，移除节点后直接恢复原计时判断。

ConsoleHistoryModule独立受[Debug]开关控制。DebugView.AcceptInput前记录原始输入，SetConsoleShow重置游标，Update在控制台显示且InputField聚焦时读取InputSystem方向键并填入命令。历史以DebugView弱引用关联，仅本次进程；游标到末尾恢复草稿，空白不记录，永不代替原版提交执行。

RoleAvailability说明.txt作为单份内嵌资源，项目与build.py均显式登记，TemplateMaintenance从资源生成同文档，保留已有JSON。cfg开关说明继续采用简短文字，详细协议放在作者说明与GAME_DATA_FORMATS。

## F9 提供者输入快照（2026-10-22）

JsonHotReloadRuntime仍调用所有注册回调，由提供者决定是否需要更新。羽毛球、地图登记、场景对象使用Core/ReloadInputSnapshot比较各自输入的内容摘要，启动时建立基线，成功后提交新基线；无变更返回JsonHotReloadResult.Skip，因此不打印完成日志、不累计成功文件/条目。没有改变公开Register接口或其他提供者的协议。

羽毛球输入为功能目录JSON/PNG；地图提供者分别选择Worldmapscfg+MapLocationscfg、SceneObjectscfg，并解析image/url中的相对PNG引用，使图片单独变更仍能刷新。两个地图快照分开，普通Mod CFG也不属于它们的输入。新增/删除通过路径集合比较发现；文件内容摘要避免同长度/时间戳变化漏检或纯时间戳保存误报。快照读取/JSON解析失败保留旧基线供下次F9重试。无变化时场景对象不会增加Revision或调用Session.Refresh。

## 插件配置分类（2026-10-19）

`PluginConfig.Initialize` 按“优化 / 机制 / 效果 / 前提 / 指令 / 替换规则 / Debug / 更新”及约定的组内顺序绑定并输出配置。`[前提]` 现绑定“新前提扩展”；`[效果]` 新增“1属性改变修复与扩展”和“新效果扩展”，三个开关独立加载且默认开启，保留已有显式值。“控制角色在列表显示”改名为“控制角色可见性”，说明按用户样例调整，运行时仍使用同一开关。迁移读取顺序为历史分类、上一轮合并分类、当前拆分分类及新名称；当前显式值优先，缺项继承旧值，保存后移除旧分类/旧名称。配置分类不改变模块实现、开关默认值或运行时加载分区。

## 属性与前提扩展（2026-10-25修订）

`Features/Effects/AttributeEffectModule` 负责 97 描述和 1001 联动修复；`NewEffectModule` 负责1/1145、999/1145，以及行动40/1524、社交20/1524、洞察999/1524与特质界面；`ConditionExtensionModule` 负责 7/2 和 101/100。由 PluginRuntime 按三个配置分别加载，不依赖其他效果开关。

复用原版 EffectorChangeAttr/EffectorSpecial 与 IncreaserAttr/IncreaserOther/IncreaserAttrPerAttr2，不添加 MessagePack Union 类型。1/1145/1沿用 otherAttrId=-1950102，步长存 format，value=0；按强化/批判数减免使用 -1950502，value=0。195050/195051 为插件的消耗修正/心情系数 RoleIncType 桶；节点按原版 effect UID 注册与撤销。关闭模块时自定义动态节点的原版默认值为0。1001 的 max 字段保存未达阈值的增长余量，实际增量从 SetAttr 成功写入结果计算。

消耗减免在 UpdateAttr 的原版附加增量合并后、属性写入前执行，Harmony transpiler 定位失败会明确报错；UpdateAttr/UpdateAttr2 的上下文防止 SetAttr 重复减免，finalizer 清理上下文。单独 GetRealAddValue 预估与 RoleMgr.HasEnoughCost 使用相同减免。101/100 保留原始 double 条件参数，避免 ConditionerUI 内部 int 字段截断 ModID。

特质布局仅在 DetailSocialView 的特质回调注册组件，窗口复用钩子仅恢复已由本模块修改的对象。槽位选择保留原版单选流程，第三方 com.lyy.traits4 存在时让出数据控制；正文、卡片尺寸和字号修改在池复用/关闭时恢复。真实 prefab 排版与输入仍待游戏内验证，详见 STATUS 和 VALIDATION。

2026-10-22：完整说明改为独立的 TraitDescriptionView（继承 CommonComfirmView），在 UIMgr 登记后使用原版 CommonConfirmView@Hint 资源，保留原版按钮/背景/字体/遮罩/动画；只对该实例正文容器增加ScrollRect/RectMask2D，原版普通确认框不受影响。原自绘黑色覆盖面板已移除。卡片保存窗口归属，关闭/复用时只关闭自身说明；原版关闭回调清理归属。

无效新扩展格式从工厂返回原版EffectorSpecial安全节点，保留装饰链与角色来源，以便两个特质文案入口显示具体错误；SpecialRun仍拒绝该节点，不登记UID、不提供效果。这不增加序列化类型，也不兼容错误简写或未定义子类型。

### 行动、减耗奖励与洞察

TraitMechanicPatches把新40/1524、20/1524工厂分派到安全的原版EffectorSpecial，持续节点使用IncreaserOther，RoleIncType=195053。HelpAction只包装原版close回调；双/三倍额外调用正向属性Effector的OnRun，无损追加行动复用HelpAction原版计数和精通段，然后同步close并跳过后续展示/事件选择，保留末尾条件与成就通知。用独立freeDepth阻止追加行动概率连锁和扣损。连续行动ID/次数保存在活动节点字段；社交回合/目标/次数另存195055历史节点，以来源角色/标签/效果参数关联，不随特质切换撤销，避免当回合上限被重置。事件社交单独将奖励延后到ShowEvent结束回调，兼顾原版不调用回调的纯效果同步分支。

CostRewardPatches使用195052保存奖励节点。通过Effector.Run装饰链识别同组增减耗；嵌套的独立效果树使用不同组号，避免洞察展开时混入其他特质。组号保存在IncreaserOther.format中。实际写入SetAttr后，以统一加法成本、扣除本组贡献的反事实成本及原版属性下限计算有效减免；预览只计算系数。奖励重新走UpdateAttr，带重入保护。增耗也是195050中带负号的修正，最后统一计算max(0,1-sum)，不会先截断减耗再加增耗。

InsightEffectPatches使用195054的IncreaserOther保存控制器及借用UID列表/来源签名（format中JSON）。Get/GetValue及Role.GetAttrMulValue读取边界刷新，处理小方法已内联的情况；线程刷新标记阻断递归。候选效果仍用原版Effector和10001登记，移除控制器时撤销子UID。新效果开关关闭时只安装RoleMgr.Load后的借用清理模块，不挂接新效果执行钩子，以免存档中的原版借用节点继续生效。复用原版MessagePack Union，不新增存档类型。

## 功能目录统一读取（2026-10-08）

`Workshop/FeatureContentDiscovery` 统一发现一个 Mod 内的功能目录：EC2BUnofficialPatch 直属目录优先，其次 Mod 直属目录，最后其他容器（可多层）。扫描不跨 Mod、不跟随 junction、匹配功能根后停止深入同名目录；每个模块保留原有 ID 冲突策略。同种增强 CFG 有多份时，运行时视图选排序第一份并警告。

资源配置绑定到实际功能目录；来源继续绑定原 ContentRoot.Id。MapExtend 的 Mod ID 改从 `workshop-<id>` 取得，不再反推目录父级。ExternalLive2D、屏幕纸条/视频/歌词、漫画、LoveDraw、羽毛球、小游戏与 BetterAudio 均接入共同目录发现；增强 LoveDrawCfg/MinigameActionCfg 和 F9 JSON 预检同步适配。RoleAvailability 单文件允许根目录或容器，旧单文件位置保留。

模板生成位置、MapExtend 本地调试例外及原版资源注册约定保持原状。BetterAudio 只接受配置目录内的相对路径；外部小游戏 DLL 以 JSON 所在目录解析，允许 `..` 指向同一 Mod 内的兄弟目录，规范化后必须仍在所属 Mod 根目录内，且不接受绝对路径。Mod 根目录从发现配置时的 ContentRoot 取得，不推断父级层数；原版 Mods/packageId 来源限定 URL 继续兼容。完整作者合同见 [GAME_DATA_FORMATS.md](GAME_DATA_FORMATS.md)。

本版的合并与构建变更见 `../../Docs/MERGE.md`；BA 与 UP 的兼容入口保留在单一 UP 程序集中，在线更新沿用 schema 1 的 `layout=merged` 清单。本文描述当前仓库结构；具体行为以源码与 `Validation_*` 为准。

## 启动与生命周期

```text
Plugin.Awake
  → PluginConfig.Initialize
  → PluginRuntime.Start
    → PluginRuntimeHost.Create（持久宿主）
    → PluginServices.Create
    → ModuleHost.Load（按配置加载 Harmony 模块）
    → UpdateService.Start
```

- `Plugin` 只是引导组件。StudentAge 会销毁挂在 `BepInEx_Manager` 上的组件，因此非退出阶段的 `OnDestroy` 不得卸载补丁。
- `PluginRuntimeHost` 是持久生命周期宿主；应用退出才调用 `ModuleHost.Dispose`、释放服务并停止更新检查。
- 每个模块拥有独立 Harmony ID。模块加载失败时会撤销该模块自己的补丁，并记录启动自检失败，不应拖垮无关模块。
- 启动时会在 `BepInEx/plugins/EC2BUnofficialPatch` 创建 UP 作者模板，启用 BA 时也会在 `BepInEx/plugins/BetterAudio` 创建 BA 模板；这些模板不参与运行时内容根扫描，运行时内容来自 Workshop Mod。BA、屏幕视频与 5002 歌词音频共用 `GameMusicPriority` 管理原版音乐通道的暂停和恢复，由持久宿主逐帧维护。
- 视频使用独立的屏幕叠加画布。剧情 CG 子视图出现时，完整覆盖视频位于其画布上方；动态背景视频则在 CG 内建立临时 Canvas，按 CG 图片实际排序层和值放在图片之上。原版文字模式下，将 `mask_talk_cg` 临时排在视频之上；若 DialogueSave ADV 已把实际文字移至独立 `DialogueSave.ADV` Canvas 且用透明 `ADV.NativeShield` 隐藏原容器，则视频层绕开该遮罩，排在 CG 图片与 ADV 文字之间，不修改隐藏容器的排序。退出 CG 时恢复原设置并回到普通对话的角色后方。播放期间原版 `DebugMgr` 所建 `DebugCanvas` 高于所有视频、CG 文字及 DialogueSave 模态层，使控制台保持可见、可点击，视频退出时恢复原层级。视频首帧淡入、正常退出淡出采用与 CG 优化相同的 0.22 秒节奏，淡出后才释放原版音道；异常退出立即清理。编辑器预览从当前 Mod 根目录读取视频注册表，并通过 `PreviewTalkView` 的有字/空文本入口执行 1164。BA 与视频的启动自检/注册数量及错误日志不受运行信息日志开关控制。

### 启动控制台与地图过渡修正（2026-10-08）

- 自动控制台等待 DebugView.Start 和 UIMgr.Init 两者完成后开启。仅本模块启用时，在 UIMgr 尚未创建视图字典的启动阶段跳过 Control.OnSubmitHide，避免 started/canceled 输入回调访问空字典；初始化后沿用原版输入处理。
- WorldMapTransitions 对不可读原版纹理使用 GPU Blit 和 RenderTexture 读回，创建归过渡对象独占的纹理；不再 Instantiate 原始纹理。恢复渲染目标并释放临时资源；复制失败仅跳过这次过渡，不能中断原版场景关闭。保留0.2秒溶解和完成/销毁清理。
- 目的地窗口继续隐藏原版旅行标识，不再输出该识别成功信息。

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

## 地图扩展与优化

2026-10-11 修正：WorldMapsRuntime 的功能160在新档 SetIsGuideMode 前登记，并在既有初始化/读档及普通CFG事务热重载后补齐；FuncData.OpenFunc 不再有写功能表的前缀。同一 TravelFunction 行不重复赋值，避免原版 SetIsGuideMode/CheckWrong 遍历 FuncCfgMap 时使枚举器失效。

SceneObjectIntroduction 只处理 UP 新增对象的介绍，不改写 MainView 原版四物件或 MapSceneView.ShowIntroduction。CommonTalkView.parms 第七项携带本次 SceneObjectSession；背景参数为-1，跳过原版 BgCfg 原图加载。介绍打开前收起所有悬停图。在 MainView 不创建对话背景，当前场景与普通图留在原版模糊遮罩之后；MapSceneView 则将当前已显示背景精灵放入对话现有 icon_bg，并在其内部创建不可点击的普通图副本。复制二维位置、尺寸、缩放、旋转与顺序，local Z 固定0，避免继承来源视图的深度而落到对话背景之后；LateUpdate 同步条件/显示效果结果。素材引用不转移所有权，介绍关闭/会话释放时撤掉副本，恢复对话背景 Image 属性与 RectTransform，避免缓存对话污染下次打开。共同 NewTalkView.OnClose 回调接收 BaseView，兼容其他继承视图。Unity prefab 与遮罩不在源码包内，实际画面需实机复测。

`Features/Mechanics/MapExtend/` 按 `[机制] 地图扩展与优化` 开关加载，默认开启。
`MapLocationRegistry` 解析 Workshop Mod 的 `EC2BUnofficialPatch/MapExtend/MapLocationscfg.json`，
不改变原版 `MapCfg` schema，不写入 Workshop 源文件。真正打开地图时只选择游戏已启用的 Mod，
确认 `MapCfg`/背景有效且没有同 ID 入口后，在原版 `MapView.OnOpen` 前创建 `_pos_<ID>`。
节点结构与 `Cell_MapItemUI` 一致，由原版创建点击/悬停监听、渲染名称和 NPC 头像、执行地点切换及关闭回收。

新增节点与原版节点一起按 priority 排序（默认新增3、原版2），同值新增排在原版后且依 mapId 顺序；原版内部相对顺序保留，逻辑 priority 为 2。
命中组件使用多边形或 PNG alpha，新增及原版节点附加射线过滤，重叠时只允许最高优先级的入口响应。
悬停图和自动描边不接收输入，不使用原版 UIButton 的矩形 EmptyGraphic 作为建筑命中区域。
schema=2 将 hitArea、visual、label 分离，地点可见性固定遵循原版规则；不提供 parentMapId，floors 继续由原有地图层级模块负责。
命中根节点按轮廓包围盒/普通图矩形建立，普通/悬停图各自使用地图位置和尺寸；悬停图无射线命中，不因描边留白扩大命中范围。
有效悬停图替换普通图，离开恢复；无普通图时叠加原背景。读取失败仅回退该图层，多边形入口保留。
标签克隆完整原版 btn_name，保留布局/图片/阴影/箭头/字体；移除 ColorInteract/EventTrigger 并重置按钮事件，避免引用原地点。
工具仅支持新 schema，可回填编辑、保留额外字段并导出 PNG。
MapLocationLabelLayout 共用父框/子层 Resize、实际字形进位和原版基准；冻结干扰作者设置的尺寸适配器。
MapLayout 辅助文件 schema=2 导出各图层基准矩形和宽高变化、PNG/九宫格/留白、原版字形图集与基线；工具据此绘制，运行时克隆原版组件。
图层 PNG 使用原版显示 Image 的有效 Sprite、实际网格/网格效果与材质离屏绘制；黑/白背景两次绘制恢复直通 Alpha，避免直接 Sprite.textureRect 截图把原始图集片段当成最终 UI。渲染目标、材质/网格副本、读回纹理在 finally 中释放。字体 Alpha 与 RGB 分开处理，浏览器按字形覆盖度设置文字颜色。labelStyle.rendering=unity-image-alpha-v1 为本次修复标记，工具要求重新导出旧辅助文件。
Image/RawImage 背景屏幕 UV 映射用于解包原图；完整截图使用屏幕映射。新字体字形需在登记后重开地图取得新辅助文件。

图片缓存和自动描边归本次地图会话所有；F9 原子替换注册表，已打开会话保留旧登记，重开后使用新资源。
关闭地图/销毁界面清理缓存与过滤组件。刷新条件失败时隐藏新增入口，保留原版 maps 字典中的格子引用直至关闭。
打开地图另导出 `MapLayout.json`（屏幕原点/局部坐标基向量/原版入口位置），输出到 plugins/EC2BUnofficialPatch/MapExtend/，供离线 `map-extend.html` 取得原版标签样式和鹅城截图坐标。新区域直接使用底图原图和 coordinateSize 定位。

## 大地图切换（1.0.23 外置测试版）

同属 `[机制] 地图扩展与优化`。WorldMapsRuntime 解析本地测试包与已启用 Workshop 内容包的 Worldmapscfg.json；MapLocationRegistry 解析同目录 MapLocationscfg.json，每个标点按 worldId 归属。作者内容与 PNG 均从 DLL 剥离；build.py/项目文件不再嵌入 CFG 或示范图片。没有外置内容时保留鹅城及原生流程。

Worldmapscfg 定义地图名/id、PNG url、坐标尺寸、defaultUnlocked、父地点/首次初始化/交通列表。MapExtend/Cfg 仅生成 MapCfg/BgCfg 注册材料，本地及 Workshop 均不读取；Func 160/type=0 由 DLL 内置，保留现有业务字段。鹅城12是固定交通登记，不自动解锁。插件集中分配交通 ActionCfg(type=1)，检查原版/Mod占用、备用分配、保存稳定映射；功能关闭时过滤特殊行动，功能开启后补齐。

WorldMapState 通过原版 union 支持的 FuncModel 在 WorldMaps.v2.worlds/maps/actions 三个独立键保存当前地图、开放状态、初始化地点和行动映射；不改变真实 FuncData。MapExtendEffects 截获 EffectorUI 的5/-5及 EffectorUseful 的1/11分支和说明，保留原版效果链顺序。defaultUnlocked第一次应用，明确锁定覆盖默认值；地点初始化只执行一次。未知地图可提前记录开放状态，待其内容启用后生效。鹅城0不能移除；当前地图移除由视图上的 WorldMapReturnDriver 等待事件/遮罩结束后返回。

Compose 先建立启用的主地图定义，再合并鹅城和 extension=true 的地点贡献。不允许重复底图定义或父地点跨图归属。缺失主定义的贡献暂不应用。地图/原版地点/背景 ID 仍由作者协调，不由行动分配器解决。

WorldTravelView 继承原版 TripUI，复用 Prefabs/UI/Main/TripView@Main、UIItemGroup/Cell_TripItemUI、滚动列表、卡片与关闭按钮；点击卡片选择，使用原版“出发”按钮；删除插件叠加的“前往”文字及图标位置改写。清除旅行星级/原版消耗/旅游业务回调，绑定区域选择与切换。窗口只包含已开放且非当前区域，包含固定返回目的地“鹅城”；列表刷新及出发时重新核验，空列表给出提示。Utils.CreateInstance 仅搜索 Assembly-CSharp，插件界面仍显式登记 UIMgr.viewDict。卡片图片由窗口自己的 TextureCache 管理。TripUI 不绑定左上角装饰标题；在本窗口实例内按标题图的显示位置/尺寸、名称定位 Image 并禁用，隐藏原版“旅行”，保留自定义“去其他地方”。源归档缺少 prefab 本体，运行时日志记录识别到的标题节点/精灵；隐藏效果待实机确认。

WorldMapSession 将原生 _pos_ 节点暂存到 group_item 外，隐藏 MapView 中不属于地点节点的大尺寸概览 Image/RawImage。新 PNG 在 group_item 前的独立同级节点绘制，与标点共用局部原点、旋转和缩放。关闭时还原 Graphic.enabled 和原生节点，释放纹理。MapLayout 导出优先引用当前自定义底图，避免误采旧图。新区域车站必须有无条件且成功创建的可见入口，否则恢复鹅城。左下角返回控件及创建代码已删除。

MapSceneView 保留原版退出事件、楼层与重开地图；交通选择结果在下次 MapView.OnOpen 前生效。若已经处于 MapView 则保留旧背景后原位重建并补调用 PlayViewAnime(true)，避免整窗关闭的回家分支。WorldMapState 使用v2独立存档键；无键/未知/未开放区域回鹅城，新开局清零。F9 同时重读两份扩展表，当前会话资源保留至重开；原版表修改和移除交通登记需重启。

原版依据：[TripUI.cs](D:/codex/assembly_ref/Assembly-CSharp/GenUI/Main/TripUI.cs:15)、[Cell_TripItemUI.cs](D:/codex/assembly_ref/Assembly-CSharp/GenUI/Main/Cell_TripItemUI.cs:29)、[TripView.InitUI](D:/codex/assembly_ref/Assembly-CSharp/View/Main/TripView.cs:33)、[TripView.OnRenderItem](D:/codex/assembly_ref/Assembly-CSharp/View/Main/TripView.cs:182)、[TripView.OnCreateItem](D:/codex/assembly_ref/Assembly-CSharp/View/Main/TripView.cs:265)、[MapView.OnOpen](D:/codex/assembly_ref/Assembly-CSharp/View/Main/MapView.cs:60)、[ActionData.AddAction](D:/codex/assembly_ref/Assembly-CSharp/ActionData.cs:660)、[MapData.UnlockMapScene](D:/codex/assembly_ref/Assembly-CSharp/MapData.cs:28)、[Utils.CreateInstance](D:/codex/assembly_ref/Assembly-CSharp/Sdk/Utils.cs:26)。相关类/方法内部核对2026-09-25托管快照；尚未确认拆分类文件指纹等同当前安装。源码包没有 prefab 本体，本机游戏目录资源 catalog 确认存在 TripView@Main。新版视觉与流程待实机验收。

WorldMapNavigation 前缀接管 Game.Back 与 Game.Esc，仅在非鹅城且最上层为 MapView 时改变导航。MapView 按 travelMaps 首项找到当前可见标点，调用原版私有 OnClickMap，保留圆形过渡、地点进入事件和进入标记；若缺入口记录日志并保留地图。本站及其内部子地点不再拦截返回键，沿用 MapSceneView.CloseView 的退出事件和 OnBeforeCloseAnime 重开 MapView；ActiveWorldId 保持不变，因而回到所属区域大地图。其他弹窗及子地点也沿用原版关闭。按原版遮罩/界面状态/enableCloseByRightClick 判断可操作时机，不直接全局禁用右键，不改 WorldMapsRuntime.SwitchTo 的主动区域切换关闭路径。源依据：[Game.Back](D:/codex/assembly_ref/Assembly-CSharp/Game.cs:314)、[Game.Esc](D:/codex/assembly_ref/Assembly-CSharp/Game.cs:338)、[MapView.OnClickMap](D:/codex/assembly_ref/Assembly-CSharp/View/Main/MapView.cs:307)、[MapView.EnterMap](D:/codex/assembly_ref/Assembly-CSharp/View/Main/MapView.cs:359)、[MapSceneView.CloseView](D:/codex/assembly_ref/Assembly-CSharp/View/Main/MapSceneView.cs:791)。内部比对2026-09-25托管快照，新返回流程尚待实机确认。

## 地图楼层兼容流程

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
- 羽毛球角色：`CustomBadmintonModule` 将同一对手 ID 的有效外置球服跨 Mod 追加，在每次打开小游戏时从原版与外置候选中等概率选择；玩家仍按旧配置确定男女模型。所选 url 对应各自的原版模板 prefab，在 prefab 实例化后、玩法读取 Armature 前，以外置 ske/tex/PNG 在全新宿主中构建 DragonBones；插槽和玩法动画校验通过后，才把新宿主切换为 Animator 下的 `Armature` 并释放旧宿主，避免 Unity 延迟销毁旧插槽时破坏新骨骼。材质从模板克隆，对象池复用到原版角色时以同样的宿主替换流程恢复模板数据。

### 尚无通用桥接

- Live2D 动作、表情、Animator Controller 与任意 Addressables prefab；地图社交默认姿态的 model3/moc3 已有专用桥接，但不是通用加载器；
- 需要多精灵图集、AudioClip 或原版资源前缀的通用小游戏资源；羽毛球单图集角色已有专用桥接，但不构成通用小游戏资源加载器；
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

## MapExtend 作者工具与模板（2026-10-08）

TemplateMaintenance 从 DLL 的 Embedded.MapExtend 资源写出 MapExtend/readme.txt 与 map-extend.html，不生成默认地图 JSON；旧 MapLocations 目录移入 MapExtend/旧版资料保留作者文件。模块显示名称统一为“机制/地图扩展与优化”，内部 key 为 mechanics.map-extend，源码目录为 Features/Mechanics/MapExtend；子地点补丁与服务归入此目录。
离线工具增加区域定义、PNG 底图、原版父地点/子地点/背景注册草稿、交通入口与自动解锁；地图切换选择独立于地点选择。鹅城使用实机 MapLayout；新区域使用 coordinateSize 与 PNG 原图直接映射，可复用鹅城导出的标签样式解决首次制作需要先进入新区域的问题。工具保存仅在网页内存，导出完整包后落盘。
完整 ZIP 使用浏览器原生 Blob / UTF-8 ZIP 存储，包含 MapExtend 的两份大地图扩展表、所引用 PNG、Cfg 三表、readme 与单文件 HTML 工具，不依赖外部库。项目文件夹导入回填两表、原版表与实际图片，不改写 Workshop 文件。WorldMapsRuntime 删除本地 Cfg 字典补充逻辑，只保留交通 ActionCfg 自动生成；作者须完成原版三表注册，工具与 readme 对此显著提示。构建同时输出独立 MapExtend 作者包。

### 本轮工具与过渡（2026-10-08）

WorldMapTransitions 在 MapSceneView.OnBeforeCloseAnime 或直接切图前复制当前背景图层。父地点直接取 icon_bg.image，非原生大地图直接取 WorldMapSession.BackgroundImage；无直接引用时才按背景名称和显示面积查找。副本在目标 MapView.OnOpen 后挂到所属 UI 层的独立 Canvas，排序高于该层普通视图，不受旧场景关闭和目标视图淡入影响。MapView.OnOpenAnimeComp 后才开始0.2秒溶解；旧视图原版0.2秒淡出期间保持背景副本。待挂载和已显示对象分开管理，目标 OnClose 仅释放已显示副本，不误删直接切图刚捕获的副本；完成、关闭、重置或捕获失败时释放纹理/精灵并终止 tween。副本不接收输入；保留原版地点退出事件和视图动画，不触发 MapView 回家关闭分支。WorldMapSession 不再输出每次打开都会重复的底图加载成功日志，底图读取失败仍报错。

MapExtend 工具相机是纯编辑状态，canvas显示矩形承担逆映射，Ctrl滚轮围绕鼠标缩放，空格/中键平移，顶点容差及控制柄维持屏幕大小。exportWorldData去掉保留内部参数；extension只输出贡献与制作坐标，参考底图不输出。原版材料仅 MapCfg/BgCfg，模板readme与导出包使用同一说明。后备描边颜色和线宽立即显示，透明命中独立设置，暂时预览不写入游戏配置。

## MapExtend 归并与场景对象

MapExtend 场景对象（2026-10-10）
SceneObjectscfg.json 新制作内容使用 schema=2，objects 数组。每项包含自动生成且不在 UI 展示的 key、作者填写的 name、所属 mapId、PersonCfg 的 personId、绑定实际背景的 bgId。
normalCondition 与 clickCondition 是独立的原版条件二维数组，空数组代表默认满足。前者只控制普通图片显示；后者控制点击和悬停，不会隐藏本来可显示的普通图。无普通图时可用多边形区域点击背景里已有的物件。
制作台输入使用原版文本，例如 1,1,1,10;100,10,100001，英文分号分组，英文逗号分参数。拒绝手写 []、空参数、非法数字及空组；导出自动转换二维数组。普通条件组沿用原版同时满足的组合规则。
click.type 只提供 introduction / introductionEffects / background / scene。introduction 仅显示 PersonCfg.introduction；introductionEffects 同次点击显示介绍并 RunEffector 执行 effects；background 的 target 为已注册 BgCfg ID，在当前地点使用原版圆形遮罩、背景资源加载、按钮/NPC/行动刷新，并将该背景保持到离开场景或原版切背景；scene 的 target 为父/子地点或家1，直接复用 MapMoveService（100,1,MapId 的 UP 实现），会解锁目标地点，不额外套同区域/原版地点 cond 限制，入口开放由 clickCondition 控制。
共用 MapMoveService 的回家分支增加原版圆形遮罩过渡；UP 原指令和对象点击一起生效，已有过渡遮罩时避免套第二层。
hitArea 和 visual 复用 MapLocationscfg 的 polygon/alpha、normal/hover PNG 协议。两图独立 position/size；hover 命中时替换 normal，移出/禁用时复位，没有 hover 时保留 normal；alpha 只根据 normal 的实际位置/尺寸检测。路径相对本 SceneObjectscfg 文件目录。
人物字段只通过“生成 / 更新原版 PersonCfg 行”按钮写入材料，保存对象不改表。每行只生成 id、name、init=[-1]、introduction、clickAudio，不夹带其他原版行或字段。未生成的人物字段修改在导出前提示，避免丢失。
四个小流程：场景 / 对象 / 点击行为 / 图片与区域，与地点页相同的并排导航。场景 MapId / BgId 和坐标宽高由作者手动填写，默认尺寸2560×1440可改；无需先创建游戏地点或导入游戏坐标文件、原版人物表。场景坐标宽高保存到 SceneObjectscfg.json 的可选 scenes 数组（mapId、bgId、width、height）；导出的模组内容通过“打开已有项目”统一读取，旧JSON没有 scenes 时按对象的MapId/BgId恢复场景并使用默认宽高。参考图仅在当前编辑会话用于定位，不导出，玩家重开时需自行额外导入；不生成 Editor 或独立制作资料文件夹。对象默认名字为 MapId-BgId-xx，也可自定义。Cfg/PersonCfg.json 仅输出对象对应的纯净原版行（id、name、init=[-1]、introduction、clickAudio），须作者自行注册；旧项目导入时剔除无关人物行及整个旧Editor目录，不将旧制作资料再次输出。
场景编辑支持比例保持的缩放、指针锚定 Ctrl+滚轮、空格/中键平移、普通/悬停图独立拖动和尺寸手柄、原始尺寸/锁比例、矩形与多边形圈选/移点/删点。浏览器悬停预览只演示图片及命中，不执行游戏剧情条件/效果。
DLL 继续读取旧 schema=1，并保留旧背景/阶段/事件队列及五种点击行为；制作台不会擅自将旧功能或事件逻辑映射为新四种行为，非空旧配置会提示重新登记。空旧模板可以导入。scene-objects.html 仍只跳转到唯一工具 map-extend.html#scene。
对象支持 MapSceneView 与家的 MainView；运行时根据注册的 mapId / bgId 及中心原点、Y向上局部坐标直接创建对象，不再生成 SceneLayout.json。场景宽高仅为编辑器参考图定位设置，需与游戏背景实际局部尺寸匹配；程序集快照不包含背景prefab尺寸，2560×1440是可调整的编辑器默认值。
推荐 Mod/EC2BUnofficialPatch/MapExtend，其次 Mod/MapExtend，再次其他容器/MapExtend；旧 WorldMaps 读取兼容。Cfg 新增须按原版流程注册并重启；扩展对象 JSON/PNG 可以 F9 重读，存档协议 WorldMaps.v2 保持。

## 统一作者制作台（2026-10-09）

map-extend.html 是唯一编辑器，不使用 iframe 或另开页面。大地图与场景对象控制器共用 retainedFiles、原版表及唯一 ZIP 导出入口；旧 scene-objects.html 跳转到 map-extend.html#scene。导入允许完整包或对象包；对象包只需模组配置、运行图片及 Cfg/PersonCfg 注册材料，无需 Editor；各原版表依旧只是注册材料。
UI 五步导航与地点内分组解耦，不要求依次完成所有功能。当前场景编辑草稿在离开步骤时校验保存，失败时保留当前编辑页；导出集中校验各模块并写入三份扩展 JSON。对象预览保留 normal/hover 独立位置尺寸；场景画布按场景宽高保持比例，悬停图替换普通图，移出后复位，与大地图及运行时一致。

## 地图表单同步（2026-10-09）

地图制作台通过 parentUnlockIds 从合并的 autoUnlockMaps 提取父地点视图，列表编辑保留子地点选择，勾选编辑仅添加/移除当前父地点。新大地图使用数字 ID/可选名称对话框，验证成功才创建记录。子地点生成器在所有校验通过前不修改 CFG、父地点 floors 或开放状态。

WorldMapsRuntime.AutomaticUnlockMaps 合并 autoUnlockMaps 与 travelMaps 父地点并去重，排除鹅城固定入口12；不追加入口 floors。交通入口自动开放强制勾选且禁用，父地点清单不能间接去掉其 ID；取消交通入口后恢复可调整且保持已有自动开放勾选。PreparePlayer 保留每存档只初始化一次的状态，不反复覆盖剧情锁定。

制作建议：建议子地点尾号从 001 开始，不建议直接使用 000 尾号。000 虽满足 ID 整除规则，工具仍允许保存，但不作为推荐范例。


## 2026-10-11 场景编辑隔离修复

场景按 MapId + BgId 分开保存和显示；当前场景的所有对象选区与图片同时保留，切换场景不会迁移对象。已保存场景可来回切换，导出后重开仍按同一归属恢复。填写或修改 MapId/BgId 后必须点击“确认场景（创建 / 切换）”，输入过程不创建场景、不切换当前编辑范围；确认的空场景也随模组保存。可在已保存场景选框下删除当前场景，含对象、参考图或自定义尺寸时再次确认；删除对象、选区、参考图及独占图片/人物行，共享内容保留；需要主动迁移对象或校正旧版误存的归属时，在“对象”页使用“调整此对象的所属场景”，图片位置、尺寸和选区保持不变。
图片与参考图异步载入绑定启动时的对象或场景，后到的旧请求不能写入新选择；删除对象或重开项目会作废对应的载入请求。切换清除拖动、悬停、顶点及文件输入状态。对象列表只列出当前场景对象；空场景清空对象表单，删除最后一个对象不会跳至其他场景。


## 2026-10-11 本地文件引用失效与编辑抢救

后续修正：地点的visual.normal/hover可为空，抢救资源遍历使用可选访问，直接从已备份的scene.objects及database.locations收集非空图片，不调用旧版不兼容空图的imagePaths；空资产记录也跳过。备份JSON保留原有null值，不清空或修改对象/选区。

导入的内容与PNG在读取成功时复制为内存Blob，导出不再依赖本地文件引用；原文件之后被移动、替换或权限变化不会影响已缓存内容。导出读取失败会显示具体相对路径，并提示保留页面及使用“抢救当前编辑”。抢救包保留当前配置/选区、未保存表单和可读取图片；不可读取PNG尽量从已解码图片重建，说明中列出重建及仍需补入的文件，不将不完整抢救包宣称为完整模组包。
原版运行JSON与注册流程不变；抢救说明及未保存表单属于紧急备份，普通模组导出不包含它们。dist/release提供可在旧版仍打开页面控制台执行的抢救脚本与说明，不需刷新原页面。

## 2026-10-11 场景确认、删除与物件效果


场景物件效果：100,11,1,PersonId 显示普通图；100,11,-1,PersonId 隐藏普通图；100,11,2,PersonId 允许点击及悬停；100,11,-2,PersonId 禁止点击及悬停。
这些指令独立于普通图显示条件/可点击条件，不改写条件。未执行指令时遵循条件；执行后相应方面以最后一次明确指令为准，显示和点击状态互不影响，并随存档保存。仍限于对象所属地点及背景。旧存档没有指令记录时继续按条件判断。
PersonId 必须已按原版注册，且在启用内容的 SceneObjectscfg.json 中能找到对象。显示/隐藏要求普通图；可点击/不可点击要求普通图或悬停图至少一张；没有所需图时不记入状态并提示日志。相同 PersonId 绑定的多个对象共享这一状态，需独立控制时使用不同人物编号。兼容运行时旧 schema=1 对象。
移动指令 100,1,MapId 已归入 MapExtendEffects/MapMoveService，复用原有父/子地点与回家过渡；同时启用移动修复和地图扩展只注册一次。

WorldMapEffects.cs 已更名 MapExtendEffects.cs；移动服务源文件归入 MapExtend。100,1 的补丁与100,11的补丁分离：移动提供者不依赖另一程序集的场景状态，物件效果由实际加载的地图模块注册。重复提供者按 Harmony 前缀类名/方法名识别；地图模块检查已加载的 MapLocationsPatches.OpenPrefix，已有提供者时跳过重复场景/保存/F9注册。独立工程D:/codex/MapExtend沿用此契约与WorldMaps.v2存档键，无UP程序集硬引用；BepInEx可选前置使UP优先加载，UP地图关闭时由独立版接管。

MapExtendCompatibility以已安装Harmony补丁识别原版CFG捕获和楼层提供者。EnsureCfgCapture仅安装一份MergeCfgsAsync捕获；RecordSource、ReloadCapturedCfg与RebuildCapturedFloors转交实际捕获者，避免两份基线/来源产生差异。RebuildFloors转换两版内部文档类型后交给实际楼层服务，使用该服务的分层配置；RootMapId同样转交。原版CFG刷新后EnsureActiveFunction对实际地图提供者恢复内置160，不在OpenFunc遍历中改表。

独立MapExtendReload把地图/对象回调接入UP的JsonHotReloadApi，转换跨程序集结果类型，不重复注册原版CFG回调。UP热重载关闭时保留独立F9。独立运行时由持久GameObject驱动更新，游戏销毁BepInEx引导组件时不撤销补丁；退出时注销自身回调与补丁。两版的模板写出共用MapExtendAuthorTemplates，资源名称、路径、迁移和保留作者文件规则一致；独立工程的sync_from_up只用于主动同步，共享源码已物理带出，构建不需要UP工程。
object-normal、object-click 为独立原版FuncModel容器，FuncSubData.isOpened保存明确true/false；无键代表没有覆写，Load/Clear清空旧会话状态。运行时刷新、悬停与实际点击使用同一覆写判定，背景归属仍是强制范围。
删除场景只清理编辑工程里的绑定内容，不删除原磁盘文件；共享PNG、人物行以及全局MapCfg/BgCfg不会作为该场景独占资料删除。
