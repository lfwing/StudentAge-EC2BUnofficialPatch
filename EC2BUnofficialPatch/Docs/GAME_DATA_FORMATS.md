# 游戏数据格式与兼容规则

本文只记录已由原版反编译源码、实际日志或 UP 实现验证的规则。未标记“已验证”的内容不得作为 Mod 兼容性承诺。

## `MapCfg`

### `floors`

- 父地点的 `floors` 中列出的每个 ID，都必须能在最终合并后的 `MapCfg` 字典中找到相应条目。
- 原版 `MapSceneView.RefreshFloor()` 直接按 ID 取字典；引用不存在的子地点会导致 `KeyNotFoundException`。
- 普通子地点可使用 `type: 2`。父地点配置 `floors: [子地点ID]` 只表示楼层/子地点入口，不会凭空创建子地点。
- 多层子地点依赖 UP 的“地图子地点扩展”开关；不开启时不要让 `type: 2` 子地点继续充当容器。
- Mod 若与其他 Mod 共同提供父子地图，必须确保各自配置在合并后完整，而不是依赖某个未启用的兼容模块来补项。

### `cond`

- `MapCfg.cond` 是地点显示、解锁或可进入时由原版条件系统判断的前提数组。
- 空数组 `[]` 表示无额外前提，不表示父子地点关系已经成立。
- 条件仅影响“是否满足前提”；它不能修复 `floors` 引用不存在导致的楼层栏字典错误。

## 关系条件：主类型 `7`

`ConditionerRelation` 以逗号分隔参数读取 subtype。参数数量不足会在构造条件时直接抛出 `ArgumentOutOfRangeException`，不是条件不满足。

| 格式 | 已验证含义 |
|---|---|
| `7,0,npcId,relationType` | 指定 NPC 的关系类型。 |
| `7,1,npcId,minFavor` | 指定 NPC 的好感度不低于值。 |
| `7,-1,npcId,maxFavor` | 指定 NPC 的好感度不高于值。 |
| `7,5,relationId,count` | 关系数据的计数判断。 |
| `7,7,value`、`7,8,count`、`7,9`、`7,11,value` | 原版支持的关系状态分支；使用前应对照当前游戏版本源码。 |
| `7,10,id,value` | 累计“以 Battle 入口启动的谈判小游戏”胜利次数达到 `value`。`id` 是结构必填位，原版判断不使用它。 |
| `7,22,npcId`、`7,40,id`、`7,101,npcId,mapId...`、`7,310,id,count` | 原版存在对应分支；须按目标版本源码核验其业务含义。 |

### subtype `10` 的限制

- 合法最小写法：`[7,10,0,1]`。
- `[7,10,541800]` 缺少第四项 `value`，会导致构造异常。
- 它不能表达“击败 NPC 541800”；若要判断指定 NPC 的谈判结果，需要独立的自定义状态/条件设计。

## `evt` 中的 `effect`

- `effect` 是事件执行时写入的效果指令，不是所有 `evt.type` 都会消费它。
- 对通知型 `type 50` 和流程型 `type 51`，`effect` 可按事件路径生效；但一旦该 `evt` 转入关联 `talk`，流程通常由 Talk 接管，不能假设 `effect` 仍会在同一时机执行。
- 配置时应按实际入口验证，不要把“某次写入后看似生效”当作通用规则；UP 可能改变或兼容原版路径。

## 社交小游戏

### 启动与结算

- `Talk` 自动打开的小游戏：由原版回调结算一次。
- `Option` 内嵌打开的小游戏：UP 1.0.20.1 在 View 关闭时优先接管并结算一次，随后原版 callback 仅用于继续 Talk。
- 两条路径都不得结算两次。失败没有独立 fail callback 时，原版可能把流程 callback 放在 success 槽，UP 需继续该流程而不能中断阶段。

### 原版玩法分类（UP 目录）

- `NativeStage`：原版 Level/`EndGame` 协议可用，如 3、5、9、10、20、24、26、28、32、41、43、46、48。
- `EmbeddedCallback`：依赖原版 callback，如 7、8、13、14、15、16、19、22、23、33、34、35、45。
- `EmbeddedRequired`：依赖 Option/Talk/Evt 上下文或额外对象参数，如 6、11、17、18、21、27。
- `SpecialCompleteOnClose`：无统一结果回调，关闭时按规则完成，如 1、2、4、29、30、31、36、37、39、42、44、47。
- 原版 dispatcher 未处理 12、25、38、40；不要把它们当作可由通用社交小游戏接口稳定启动的玩法。

## 外部屏幕资源

### `5001`：ScreenPaper

- 文件搜索位置：内容根目录的 `EC2BUnofficialPatch/ScreenPaper/Custompaper.json` 或 `ScreenPaper/Custompaper.json`。
- 格式：`{ "papers": [{ "id": 正整数, "image": "相对图片路径" }] }`。
- `id` 必须已存在于最终原版 `PaperCfg`；插件只覆盖图片，不注册纸条文本。
- 图片必须位于同一 ScreenPaper 目录内，仅支持 PNG/JPG/JPEG；禁止绝对路径和用 `..` 逃离目录。
- 多个 Mod 声明同一 ID 时，该 ID 的自定义覆盖全部禁用并回退原版。

### `5002`：ScreenLyrics

- 歌词项必须有正整数 `id` 与非空 `text`；`text` 中的 `\\n` 会转换为换行。
- `audio` 可省略；若填写，必须是正整数且对应可用音乐 ID（包括 BetterAudio 注册的可用路径）。
- 多个来源声明同一歌词 ID 时，该 ID 标记冲突，不选择“后加载者覆盖前加载者”。

## Live2D 与静态立绘

- 原版使用 Cubism：人物配置含 `l2d`/`l2d2` 与 `l2dParm`/`l2dParm2`，常规加载目标为 `l2dmodels/<name>/<name>` 的 Addressables prefab。
- 该 prefab 依赖 Cubism 组件、遮罩纹理、动作与动画控制器；普通 PNG 不能直接作为外部 Live2D 模型。
- 1.0.21 的外置 L2D 只用于地图社交界面中的静态 Mod 角色，可驱动标准参数的待机、眨眼、呼吸和鼠标跟随，并读取有效 `physics3.json`；不读取 motion3、表情或 Animator Controller，也不接入 Talk。

### 地图社交界面外置 Live2D

- 玩家开关：`[机制] 地图社交界面外置Live2D = true`，默认开启。
- 配置位置：`EC2BUnofficialPatch/ExternalLive2D/ExternalLive2D.json`。插件本地目录与 Workshop Mod 根目录下都可使用。
- 推荐格式：`{ "models": [{ "personId": 1001, "clothId": 0, "schoolStage": "primary", "model": "1001/cloth0/primary/model.model3.json", "fitScale": 1.0, "xOffset": 0, "yOffset": 0, "staticAnchorX": 0.5, "staticAnchorY": 0.5, "modelAnchorX": 0.5, "modelAnchorY": 0.5, "flip": false }] }`。
- `personId` 必须为正整数；`clothId` 为非负整数。`schoolStage` 为 `primary`、`middle` 或 `all`，省略等同 `all`。当前学段精确项优先于 `all`；相同角色和服装可分别登记小学与中学模型。
- `model` 必须是相对于 `ExternalLive2D.json` 的 `.model3.json` 路径；不允许绝对路径或 `..`。模型包内引用同样不得离开 `model3.json` 所在目录。
- 最小运行时资源为 `model3.json`、其引用的 `moc3` 与全部 PNG 纹理；`physics3.json` 可选。`cmo3`、`motion3.json`、`exp3.json` 和 Animator Controller 不由第一版加载。
- 未填写旧 `scale/x/y` 时使用自动适配：读取静态立绘经 `PersonCfg.urlParm/urlParm2` 处理后的 Rect，排除透明像素，以可见高度和当前可见 Drawable 高度计算基础倍率。`fitScale` 默认 `1`；`xOffset/yOffset` 默认 `0`。
- `staticAnchorX/Y` 与 `modelAnchorX/Y` 均为 0～1，默认 0.5；X 从左到右、Y 从下到上。它们用于让构图不同的静态图与模型对齐同一语义位置。旧 `scale/x/y` 不能与自动适配或锚点字段混用。
- 模型存在标准参数时，模块驱动 `ParamAngleX/Y/Z`、`ParamEyeBallX/Y`、`ParamEyeLOpen/ROpen`、`ParamBodyAngleX/Z` 与 `ParamBreath`；缺失的参数单独跳过。有效 `physics3.json` 在这些输入之后运行。
- 遮罩控制器使用游戏 `GlobalMaskTexture`，直接写入字段后重建全部 Junction，再由 OnEnable 为新 Junction 分配 MaskTile。相同模型重复打开时复用实例。成功日志按模型在单次游戏进程内去重，失败日志不去重。
- 任一必需文件、PNG 解码、moc 一致性/版本、Cubism 实例化或渲染绑定失败时，销毁外置实例并继续显示当前静态服装；静态服装自身缺失时再按下节规则回退 0 号常服。
- 多个配置重复登记同一 `personId + clothId + schoolStage` 时，该组合的所有外置 L2D 声明全部禁用；不同学段不构成冲突。

### 地图静态 Mod 角色服装差分

- `ModFaceCfg.id` 继续使用原版 `角色ID * 1000 + 服装编号 * 100 + 表情编号`；1.0.21 不新增全局服装语义或编号范围。
- 普通地图的 `MapRoleView` 对静态 Mod 角色使用当回合 `Role.ClothId`；学校地图仍强制使用 1 号校服。
- 非 0 服装必须存在表情 0 对应的 `ModFaceCfg` 图片且实际文件可访问；否则回退 0 号常服。
- 服装解锁仍通过原版效果加入角色服装池。剧情 3006 只临时改变 Talk 立绘，不解锁服装，也不改变地图随机服装。

## Debug 配置

- `[Debug] AutoOpenGameConsole = false`：默认维持原版行为。
- 设为 `true` 后，`DebugView` 创建完成时启用并显示游戏内调试控制台；反引号切换和 Esc 关闭继续使用原版输入逻辑。


## 1.0.23 接手补充

外部小游戏 Context 的 Begin/Cancel/失效事件与 deferStart 参数见 ../../docs/MINIGAMES.md。独立 NPC 键与阶段编号不变。社交资料 Mod 静态立绘使用 GetRoleUrlParms 的学段回退规则，参数顺序仍是 X、Y、scale；缺项/非有限值用 0、0、1，非正scale用1，不改变源 JSON。

自动更新改为 product=StudentAge.BA.UP/schema=2，两种布局的文件列表分别提供，游戏程序集兼容性采用 SHA-256。旧单 DLL schema=1 被拒绝，详见 ../../docs/AUTO_UPDATE.md。
