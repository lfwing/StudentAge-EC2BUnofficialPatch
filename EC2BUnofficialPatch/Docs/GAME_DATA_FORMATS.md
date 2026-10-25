# 游戏数据格式与兼容规则

## 属性效果、行动与特质扩展（2026-10-25）

以下为UP 1.0.23.1当前实现；[本版更新](CHANGELOG_1.0.23.1.md)汇总变更与迁移说明。完整数组写入原版效果字段（List<float>）；前提仍为List<double>，不能混写。百分比参数写35表示35%，仅原版97的倍率增量使用0.1表示10%。

| 配置开关 | 标准格式 | 游戏内说明与作用 |
| --- | --- | --- |
| `[效果] 1属性改变修复与扩展` | `1,97,条件属性,阈值,目标属性,倍率增量` | 当条件属性达到阈值时，改变目标属性获取率；修正文案。 |
| 同上 | `1,1001,每次点数,来源属性,奖励点数,目标属性` | 每累计实际增长指定点数的来源属性，获得目标属性，余量保留。 |
| `[效果] 新效果扩展` | `1,1145,1,attr1,x,attr2,y,z` | 每x点attr1增加y%的attr2获取率，最高z%。 |
| 同上 | `40,1524,1,x` | 连续进行同一项具有属性收益的行动时，有x%的概率获得双倍属性。 |
| 同上 | `40,1524,2,x` | 进行具有属性收益的行动时，有x%的概率视为额外无损行动一次。 |
| 同上 | `40,1524,3,x,y` | 连续进行同一项具有属性收益的行动时，有x%的概率额外消耗相当于精力恢复上限y%的精力，获得三倍属性。 |
| 同上 | `20,1524,1,x,y` | 每回合首次与某位关注对象完成社交后，心情+x，每回合最多触发y次。 |
| 同上 | `999,1145,1,attr1,x` | 减少x%的attr1消耗。 |
| 同上 | `999,1145,-1,attr1,x` | 增加x%的attr1消耗。 |
| 同上 | `999,1145,999,x` | 每次减少某属性消耗后，获得相当于实际减免量x%值的随机一项指定属性。 |
| 同上 | `999,1145,2,attr1,x` | 每个强化/批判的方法论/价值观，减少x%的attr1消耗。 |
| 同上 | `999,1145,3,x` | 心情带来的三维获取率加成系数额外提升x%。 |
| 同上 | `999,1524,1,x` | 好感度排名第x的关注对象，其已解锁的挚友备选特质同时生效。 |
| `[前提] 新前提扩展` | `7,2,角色ID,名次` | 该角色是已结识角色中好感度第几名。 |
| 同上 | `101,100,ModID` | ModID位于原版activeMods启用列表中。 |

游戏内特质说明使用上表简化句式，并代入实际属性名称、数值与百分比；不在说明后追加第二次起算、关系门槛、负向上限或随机属性清单等补充文字。对应机制和限制仍按下文执行，没有因文案简化而变更。

本轮编号替换：原`1,102,...`改为`1,1145,1,...`，原`999,50,...`改为`999,1145,...`；旧编号不再作为扩展入口，不提供别名。`1,1145`只接受带子类型1的八项格式，不接受七项误写。消耗模式1/-1/2必须五项，3/999必须四项；不兼容四项固定减免简写。旧档中已登记的合法增益节点仍可继续计算及按UID撤销，编号调整不会自动改写Workshop CFG、替换已选特质或清空存档增益。修改已选特质CFG后，需要切换其他特质再切回以重新登记。

全部参数必须是有限数，属性ID必须为PersonAttrCfg中存在的整数。消耗增减模式1/-1/2还要求consume=1或2；运行时再次检查，旧档中误挂在consume=0属性上的节点不生效。属性获取率步长x>0，y/z≥0；行动概率x范围0～100；额外消耗比例y≥0；社交上限y为非负整数；洞察名次x为正整数。无效新格式保留安全说明节点，显示“此项不生效”，不登记增益UID。

### 获取率与消耗结算

- 获取率增量=`min(z,max(0,floor(attr1/x)×y))%`，随来源属性当前值动态计算；上限为0时不提供加成，只影响正向属性获取。
- 模式与属性分开：`999,1145,1,7,20`中第3项1是固定减耗模式，第4项7才是精力ID；模式-1是单独的增耗模式，不传负百分比。原版参考CFG中，consume=2为心情0、信任3；consume=1为精力7、热情11、探索点20、零花钱30、动力301、策略点303。自定义属性也可以声明consume=1/2。
- 同一属性所有增耗与减耗先加法相消：最终扣量=`原应扣量×max(0,1+Σ增耗-Σ减耗)`。增耗20%与减耗20%相消为原成本；增耗20%、减耗120%时扣量0，不能先将减耗截至100%后再加增耗。负向变化在合并原版AttrAddInc后统一处理；UpdateAttr、UpdateAttr2、直接SetAttr及HasEnoughCost预检不会重复计算。保留原版属性下限规则，心情等允许为负的数值不会被统一改为最低0。
- 强化/批判数量分别统计StudyData.methods中的mode=1/2、ValueviewData.groups中Enable且mode=1/2的节点，各按srcId去重后相加；方法论和价值观是两个独立集合，即使数字ID相同也分别计数。默认/未启用的价值观不计，状态变化后立即重新计算。
- 减耗奖励与其减耗效果写在同一特质effect数组内，顺序不限。以同次效果装饰链的来源组关联自身贡献；其他特质及嵌套的独立效果组不计入。单独写奖励而没有同组减耗时不发奖励。同组增耗与减耗先相消，按加入该组减耗前后的实际扣款差计算；其他来源已覆盖全部成本时，自身贡献为0，不再奖励。属性下限截掉的、实际无法扣除的部分不算节省。
- 随机范围固定为0、1、2、3、4、7、11、30（心情、智力、情商、信任、体魄、精力、热情、零花钱），各项等概率。30精力成本减少20%得到6点实际减免，x=30时随机属性基础奖励1.8；奖励接入原版UpdateAttr获取率与属性变化提示，不另行返还6精力。预览、资格检查不发奖励，奖励过程不递归产生减耗奖励。
- 心情系数先取原版605结果b（保留功能门槛及取整），额外比例之和为s。b>0时为b×(1+s/100)，b<0时为b×max(0,1-s/100)；400心情、额外15%得到+46%，-300心情得到-25.5%，负向最多减免100%。
- 1001按SetAttr实际写入后的正向增长累计，包含361及UpdateAttr2/直接SetAttr；达到上限未写入的部分不计。余量使用原版节点字段存档，不补发旧档漏记历史；奖励仍经过UpdateAttr并防止联动循环。原版金钱消耗分支保留。

### 行动与社交边界

- 普通行动的cfg.effect至少包含一项`[1,1,有效属性,正数]`才参与行动效果，包括打工挣钱、吃饭恢复精力。排除type=1/2/3及funcId、evtId、minigameId、evtType非0的入口和3192上课入口。原版参考例：3111自习、3121闲聊、3131图书分类、3141跑步、3171隆江猪脚饭。
- 连续效果只从同一行动第二次起判定。执行其他行动或完成社交打断；未通过成本检查的行动尝试不算执行。换成进阶行动的新ID也重新计算。连续记录保存在原版可序列化增益节点中。
- 双倍/三倍只额外执行正向`1,1`属性奖励各一次/两次，保留原版获取率、固定附加增量和联动；其他奖励、负向效果、行动次数与精通不重复。三倍的额外精力成本以当时的属性9“精力恢复上限”计算（不是属性7的上限，也不是属性8恢复值），同样受全部精力增减耗修正；额外成本无法支付时不发未付款的三倍奖励。
- 额外无损行动复用原版HelpAction计数、精通、奖励及精通完成处理，cnt/totalCnt/yearCnt和打工计数正常增加。跳过成本、事件抽取、行动展示、小游戏阶段及额外属性扣减；不再触发本轮新增的概率行动效果，也不生成减耗奖励。额外行动可以跨过原版精通门槛；奖励及进阶提示仍沿原版流程。
- 社交奖励在普通社交完成回调或事件社交结束回调后结算；纯效果事件同步执行完成后结算，打开事件时不提前发奖。当前Relation>0的关注对象才参与；同一目标每回合首次完成最多奖励一次、总次数受y限制。目标记录和次数使用独立的原版可序列化历史节点存档，回合变化时重置；切换特质或洞察重建不会清空同一来源的当回合上限。

### 洞察：挚友备选特质同时生效

只在Relation>0的关注对象中按Favor降序排名，排除主角。并列采用1、2、2、4的名次规则；某名次有多人时选角色ID较小者，跳过的名次没有目标。目标必须达到原版GetTraitNeedRelation要求；不因排名而越过关系门槛。

只展开该角色实际Traits列表中显示的前三/四个已解锁候选，按当前顺序取值；不根据TraitsCfg猜测全部同角色特质。不重复已选Trait、不复制Trait2（至交），不会同时保留同轴替换前后的旧卡片；剧情仍需用20,63正确替换当前旧ID。候选中的递归999,1524控制效果被剔除，其他数值效果保留。

借用的数值效果通过原版SetIsInc、rateType=10001和来源角色登记UID，保留原版倍率与关系判定。排名、关注状态、候选列表或当前选择变化时，下一次数值读取前撤销旧UID并重建必要的候选效果；多条洞察不重复叠加同一候选。UID与来源签名随节点存档，移除洞察时一并撤销借用效果。关闭“新效果扩展”后读档也会撤销已保存的借用效果；不会动原本选中特质的UID。

### 示例

```text
[1,1145,1,361,10,2,2,20]  每10点二次元浓度，情商获取率+2%，最高20%
[40,1524,1,35]             执着的行动效果
[40,1524,2,35]             坚韧的行动效果
[40,1524,3,25,5]           偏执的行动效果
[20,1524,1,2,3]            细腻的新增社交效果
[999,1145,1,7,20]          精力消耗减少20%
[999,1145,-1,7,20]         精力消耗增加20%
[999,1145,999,30]          与同一特质的减耗效果配合使用，随机属性奖励为自身实际减免量的30%
[999,1145,2,3,4]           每个强化/批判的方法论/价值观减少4%信任消耗
[999,1145,3,15]            心情带来的三维获取率系数额外提升15%
[999,1524,1,1]             好感第1的关注对象的已解锁挚友备选同时生效
[7,2,102,2]                角色102好感排名第2
[101,100,3672553808]       Mod 3672553808已启用
```

### 前提与数字精度

- 7/2 排名池为 RoleMgr 已存储、Relation>0 的已结识角色，排除主角0；未结识/缺失角色和非正整数名次均失败。同好感共享名次，采用1、2、2、4的排序方式；150/210/300三人中，210为第2，`[7,2,102,3]`失败。多个条件仍由原版按 AND 组合。
- 101/100 直接读取原始 double 参数，校验正整数后转 ulong 与 activeMods 比较，不使用原版构造器缩窄后的 int 字段，也不转 float。十位 ModID 可精确保存；JSON中直接写数字 `3672553808`，不要加引号，不要经效果 float 数组转存。接受的安全整数范围为1～9007199254740991；小数、无效数值、超范围均失败。查询启用列表不等于声称其全部资源已成功加载。

### 同开关的特质界面优化

“新效果扩展”还负责特质正文约束、自动换行与字号适配，极长内容提供“查看完整说明”滚动入口。已解锁列表达到4项时显示前4项，否则保留原版3槽；不修改候选列表、不自动解锁。通常仅所选特质生效；使用下述洞察效果时，可额外生效目标已解锁的备选。第5项及以上暂不提供分页。第三方 `com.lyy.traits4` 存在时让其负责候选数据。界面实现已构建，真实 prefab 画面、点击与不同分辨率仍需游戏内复测。

完整说明从2026-10-22起复用原版 `CommonConfirmView@Hint` 预制体的背景、标题、字体和关闭按钮，仅在正文区加入滚动；不再使用自绘黑色面板。

特质机制：`TraitsCfg.effect` 参与角色详情和候选卡片的文案生成；只有实际选择时，`Role.SetTrait` 才以主角0为受益者、NPC为来源运行效果，并保存UID供切换撤销。显示了候选不代表其已经生效。热改CFG后，如果仍选择原ID且已有UID，原版会提前返回；先换其他特质再切回，才能重新登记本次修改的效果。新增属性还必须提供对应的PersonAttrCfg，不能只增加特质行。

历史错误模式7不会自动改成属性7或固定减免；本轮固定精力减免应明确填写`[999,1145,1,7,百分比]`。错误数组有可见提示，不修改Workshop原文件。

## 特质切换与角色可见性（2026-10-25）

- `[20,5418,1,1]`：特质卡片可随时切换，无冷却限制。归入“新效果扩展”；只解除计时，保留原版关系门槛与可选卡片。作为特质时按原版UID撤销，原切换回合记录保留。
- `[22,999,2,personId]` / `[22,999,3,personId]`：显示关注对象 / 隐藏角色，最近一次指令覆盖默认cond，不自动关注、不改变考试或空间。
- `[23,3,1,contentId]` / `[23,3,-1,contentId]`：显示 / 隐藏已发布动态或日志，图片跟随父项。
- `[23,3,2,commentId]` / `[23,3,-2,commentId]`：显示 / 隐藏已发布评论；末项不是留言ID。
- `[23,3,999,roleId]` / `[23,3,-999,roleId]`：按作者批量显示当时已发布但隐藏的内容 / 隐藏当时已发布且显示的内容，包括动态、日志、评论、留言/回复、署名点赞及附属图片。包含在别人空间的留言与互动；匿名点赞没有作者可供归属。后发内容不继承批量决定。

22/999与23/3随“控制角色可见性”开关加载，无须额外启用新效果扩展。均为严格四项；不调用发布或删除，不补发未发布内容，不更新原始时间。父项隐藏时子项不可见，不擦除子项自己的显示决定。正文和相册过滤同一发布集合，原版页面类型/所属空间筛选继续保留，同时间戳以内容ID稳定排序。

`RoleAvailabilityCfg.json`示例：

```json
{"roles":[{"personId":114514,"cond":[[1,1,1,100]],"takeExam":false,"hiddenkzone":true}]}
```

`hiddenkzone`可选，默认false，仅控制该作者空间内容的默认显示；不影响实际发布，独立于社交cond。`personId`与`takeExam`必填；cond省略/null/[]不增加默认条件，示例是主角智力至少100。考试保留原有准入与完整性检查。重复角色冲突项禁用并记录日志。完整字段及组合示例见[RoleAvailability说明.txt](../ModAuthorTemplate/RoleAvailability说明.txt)。

社交、空间显式决定按当前存档保存；最近一次实际修改该项的指令高于默认配置，F9替换默认配置不清除决定。批量只改需要改变状态的既有项，不把已符合目标状态的项额外冻结为覆盖。关闭模块并重启回到原版展示，再启用仍可读取本档决定。新游戏/其他存档不继承。

## 控制台历史（2026-10-25）

`[Debug] 控制台调取历史记录`默认true。仅原版DebugView控制台可见且输入框聚焦时响应上/下，调取启用后本次运行已提交的非空命令，保留原始大小写；下键越过最新项恢复未提交草稿。记录不跨游戏进程保存，不从屏幕日志反解析；方向键只填入命令，执行仍用原版提交方式。该开关独立于AutoOpenGameConsole，不自动启用控制台入口。

## 功能目录统一读取（2026-10-08）

作者只需复制使用的功能目录，无需复制整个 EC2BUnofficialPatch。单个 Workshop Mod 内支持以下布局，推荐与扫描顺序均为 2 → 1 → 3：

1. `<Mod>/功能目录/`
2. `<Mod>/EC2BUnofficialPatch/功能目录/`（最推荐，便于区分与查找）
3. `<Mod>/任意名称的容器/功能目录/`（支持多层容器）

适用目录：BetterAudio、comic、custombadminton、ExternalLive2D、LoveDraw、Minigame、ScreenLyrcis、ScreenPaper、ScreenVideo、MapExtend。目录及配置文件名按大小写不敏感匹配；功能目录内部结构与 JSON 字段不变。同名功能目录嵌套时只登记最外层，不把内部资源目录重复登记；不跟随目录链接或 junction 到其他位置。

- 实际资源目录与所属 Mod 分开记录；大地图的 Mod ID、启用过滤及 Live2D/音频来源均不由容器名字或父目录层数推断。
- JSON 内图片、视频、模型、音频、时间轴和外部小游戏 DLL 使用相对于该 JSON 所在功能目录的路径。LoveDraw/comic 的短路径以各自功能资源根为准；原有 `Mods/<packageId>/...` 来源限定路径继续兼容。移动整个功能目录且保留内部结构即可，无需修改这些相对资源路径。BetterAudio 不接受绝对路径及 `..`；external 小游戏 DLL 不接受绝对路径，但允许 `..` 引用同一 Mod 内的兄弟目录，最终规范化路径必须仍在所属 Mod 根内（例如 `EC2BUnofficialPatch/Minigame` 下的 `../../plugins/CampusMinigames.dll`）；内置校验音频自己的缓存路径不受影响。
- 增强 `LoveDraw/LoveDrawCfg.json` 与 `Minigame/MinigameActionCfg.json` 在三种布局均可替换/注入同一 Mod 的运行时 CFG 视图，不改写 Workshop 原文件。同一 Mod 同一种增强 CFG 有多份时，按 2 → 1 → 3 选第一份并警告。其余模块保留原来的 ID 冲突策略，不保证重复内容自动合并或推荐目录覆盖全部资源。
- 单文件 `RoleAvailabilityCfg.json` 同样支持推荐容器、Mod 根目录和其他容器。旧 CustomMinigamecfg.json/CustomLyrics.json 位置继续兼容。
- 启动读取、编辑器 ScreenVideo 预览、现有 F9 JSON 预检使用相同的功能目录发现方式；F9 原有模块回调及音频重启要求不变。修改 PNG/视频/DLL 的生效时机仍按对应模块规则。
- 模板生成流程及位置不变：UP 在 `plugins/EC2BUnofficialPatch`，BetterAudio 在 `plugins/BetterAudio`；模板只供参考复制。MapExtend 本地调试目录例外仍保留；旧 WorldMaps 按下述规则兼容。
- 原版 `Cfgs/zh-cn`、`Audios`、`Textures` 不属于本次 UP 功能目录发现。原版 CFG 注册与 `Mods/<packageId>/...` 逻辑 URL 仍遵循游戏规则；更换它们的物理位置需要相应更改原版资源 URL。`MapExtend/Cfg` 仍仅是作者注册材料，不由 UP 自动加载。


本文只记录已由原版反编译源码、实际日志或 UP 实现验证的规则。未标记“已验证”的内容不得作为 Mod 兼容性承诺。

## 4016 漫画资源扫描（2026-10-08）

- 只索引 `comic/<漫画子文件夹>/` 中的 PNG/JPG/JPEG；comic 根目录文件、`comic说明.txt` 和其他非图片文件均忽略，不计入不规范文件数。
- 漫画子文件夹内的图片仍须符合 `{图号}-{分镜号}` 命名；分镜数量、重复图片及跨 comic 目录冲突继续校验。

## `MapCfg`

### 大地图新增入口登记

- 原版 `MapView.OnOpen` 按 prefab 的 `group_item/_pos_<地点ID>` 建立入口，MapCfg 没有位置/图片/优先级字段。
- `[机制] 地图扩展与优化` 读取 Mod 内发现的 `MapExtend/MapLocationscfg.json`（三种布局均支持），仅支持 schema=2；未公开发布，按用户要求不兼容 schema=1。
- 每行 worldId 指定所属区域（默认0为鹅城）；mapId 必须已由 MapCfg 注册且具有有效 BgCfg；不替换已有入口，不支持 type=1。没有 parentMapId；本模块创建独立大地图入口，子地点继续由 MapCfg.floors 注册。
- priority 默认 3，至少 1；原版逻辑值为 2，保留内部顺序。同值新增排在原版之后，新增之间较大 mapId 优先。显示顺序与射线命中使用同一规则。同文件重复 ID 为错误，跨 Mod 同 ID 保留第一个已启用 Mod 登记；已启用 Mod 标点优先于本地测试包。
- `hitArea.type=polygon`（默认）：points 为 group_item 局部坐标中的绝对顶点，3～256 点，允许凹多边形，不允许重复点/自交/自接触。
- `hitArea.type=alpha`：以 visual.normal 的透明度命中，alphaThreshold 默认 0.1，范围 (0,1]；不使用悬停图命中。
- `visual.normal`、`visual.hover` 可省略/null；提供时各自声明 PNG 相对路径 image、中心 position、正数 size。图片和命中区域各自定位，不要求多边形位于图片矩形内。
- 鼠标进入显示 hover；有 normal 时替换普通图，移出恢复 normal；无 normal 时覆盖原地图，移出隐藏。不显示常驻轮廓。
- hover 缺失/读取失败使用 `visual.fallbackOutline`：color 默认 #FFE89FFF，width 默认 3，范围 (0,32]。polygon 按顶点描边，alpha 沿普通图轮廓描边。PNG 损坏不因悬停资源问题移除入口；alpha 模式普通 PNG 无效时跳过。
- label 使用完整原版 btn_name 克隆，保留边框/箭头/阴影/字体/NPC 容器，清除源入口交互引用。text 默认 MapCfg.name，visible 默认 true，position 默认命中中心，backgroundColor 默认 #00BFE8FF，textColor 默认 #FFFFFFFF，scale 默认 1（范围 (0,10]）。size 省略时原版高度、文字自动扩宽，提供时两项均正。
- 移除 enabled 和 visibility.requireUnlocked 开关。标点始终遵循原版 `(解锁 && cond) || 存在NPC`；大地图是否开放由独立区域状态控制。
- 工具支持普通/悬停 PNG 独立导入、等比尺寸与拖动定位、原版标签外形示意/颜色/位置/缩放、顶点拖动/删除、悬停预览、登记回填和保留未编辑字段。工具在 MapExtend 模板目录提供“大地图与切换 → 父地点注册 → 标点与悬停 → 导出安装”流程；可导入整个 MapExtend 文件夹，导出包含两份扩展 JSON、实际引用 PNG、原版 Cfg 两表、readme 与工具的 MapExtend.zip。单张下载 PNG 仅为额外备份。工具使用紧凑步骤侧栏和固定地图，圈选/移点/删点独立；编辑时悬停图常显并提供图层矩形/缩放控制点、5% 微调、移到圈选中心，预览时才按命中切换。编辑显示开关/控制框不导出。
- `MapLayout.json` 辅助文件改为 schema=2（与地点登记的 schema 独立）：导出实际原版标签基准尺寸、各图片层矩形/宽高变化、九宫格边框/透明留白、字形图集/进位/基线/阴影及背景屏幕 UV 变换。工具和运行时共用标签尺寸合同，取消 180×40/22 的预览硬编码。
- 标签图层 PNG 按原版显示组件的实际网格/材质绘制，不直接截取 Sprite 原图；绘制结果包含材质外形、UV/网格效果和原图透明留白，padding 归零，九宫格边框换算为已绘制图层的像素/局部单位。字体图集使用 Alpha 蒙版着色。`labelStyle.rendering=unity-image-alpha-v1` 标识本次修复；旧辅助文件即使 schema=2 也需重开地图重新生成，工具拒绝旧标记，地点登记不受影响。
- label.position 为父框中心，size 为父框尺寸；省略 size 时原版基准按字形进位扩宽，scale 同时缩放所有子层。冻结与作者尺寸冲突的布局适配器，文字保持原版字号/单行；字号通过 scale 调整。辅助文件之外的新字形暂用浏览器字体，登记后重新导出辅助文件可取得原版字形。
- 工具明确选择截图或解包原图：截图用屏幕映射，原图用实际 Image/RawImage 背景 UV 映射；无法识别背景时要求改用完整截图，不把同分辨率当成同坐标。旧辅助文件需重新生成，地点登记仍为 schema=2。
- F9 下次打开地图生效，语法/几何/非法路径错误保留上一份成功注册表；缺失/损坏 PNG 打日志并按图层回退。制作流程见 `ModAuthorTemplate/MapExtend/readme.txt`。

### MapExtend 配套工具输出（2026-10-08）

- 模板目录 plugins/EC2BUnofficialPatch/MapExtend/ 只写 readme.txt 和 map-extend.html；运行时另在这里导出 MapLayout 辅助资料。工具保存仅在内存，完整导出后写入磁盘。
- 完整包根目录为 MapExtend，包含 Worldmapscfg.json、MapLocationscfg.json、实际引用 PNG、Cfg/MapCfg.json/BgCfg.json、readme.txt 和 map-extend.html。MapLayout 不输出到运行包。
- MapExtend/Cfg 全是原版表，只生成、完全不由插件读取；必须按原版注册，不能用原版表的生成代替安装。此规则同样适用于 plugins 本地调试目录。大地图底图无需 BgCfg；内部场景背景和父子地点仍由原版 MapCfg/BgCfg 注册。
- 新大地图用图片原图和 coordinateSize 定位，复用 MapLayout 的原版标签样式；鹅城保留实机截图/解包原图映射。扩展 schema 与字段保持不变。

### 外置多大地图内容包（1.0.23 当前规则）

- Worldmapscfg.json schema=1：worlds[].id/name/url/coordinateSize/defaultUnlocked/mapIds/autoUnlockMaps/travelMaps。不提供 functionId、actionBaseId、autoUnlock 或可编辑鹅城交通入口。
- 地图 ID 为数字 0～16777215，以适配原版 List<float> 效果；0 固定鹅城，交通父地点12由 DLL 固定注册且不默认开放。Func 160/type=0“去其他地方”由 DLL 保留原有名称、图标等字段内置，作者不得修改。特殊行动 type=1/funcId=160 由插件分配 ID 并保存映射，功能开启后补齐，不在开局强制解锁功能。
- [101,1,160] 开启功能；[101,4,12] 仍按原版开放客运站。[101,5,地图ID] 开放新地图；[101,-5,地图ID] 移除前往资格，效果说明同步扩展 EffectorUI.OnToString。沿用原版 EffectorUI，不增加 MessagePack union 类型。
- 地图开放状态独立于 Func/地点，随存档保存；提前解锁地图后再开启功能会显示该地图。defaultUnlocked 缺省false，首次认识地图时应用；明确移除后不会由默认值重新打开。目的地只显示已注册、已启用、已开放、非当前地图；点击出发再次核验。鹅城0不能移除，当前新地图被移除后等待剧情/遮罩结束回鹅城。
- autoUnlockMaps 只初始化一次。父地点清单强制包含 travelMaps 入口；其 floors 不隐式追加，子地点仍独立选择。工具勾选交通入口后锁定父地点自动开放，任意一边不能移除该入口的自动开放资格。鹅城12始终排除自动初始化，不因功能开启解锁所有车站。移除地图不删除地点进度、CFG或图片。
- 不同 Mod 可共同贡献鹅城地点。同一新增地图只有一份底图定义；其他 Mod 使用 {id,extension:true,mapIds,autoUnlockMaps,travelMaps}，不重定义底图/默认开放。coordinateSize 可作为工具对齐辅助保留，但不覆盖主地图尺寸。缺少启用的主定义时暂不应用该贡献。重复完整定义或跨图父地点ID冲突报错。原版 MapCfg/BgCfg ID 仍须作者协调。
- MapLocationscfg.json schema=2：locations[].worldId/mapId/priority/hitArea/visual/label，移除 enabled/visibility。父地点仍须原版 MapCfg/type=0/bg/floors，子地点不单独打标点。PNG路径相对来源文件，各 Mod 同名图片互不覆盖。
- 新地图需无条件车站：travelMaps 非空、属于 mapIds，MapCfg.type=0/cond=[]，有效背景及可见标点标签。非鹅城右键/Esc进入首站，车站内部再次返回所属大地图，正常返回鹅城经交通行动选择。保留原版旅行式卡片、出发和关闭控件。
- WorldMapTransitions 保留退出背景副本，挂在所属 UI 层的独立 Canvas；目标地图原版淡入完成（OnOpenAnimeComp）后才以0.2秒溶解，避免与原版淡入同步而掩盖过渡。父地点使用实际 icon_bg.image，非原生大地图使用实际底图。直接切图补调用 PlayViewAnime(true)，不触发回家关闭分支。副本不接收输入，独占纹理在动画结束、关闭或重置时释放；CFG 无需调整。
- 运行目录：启用 Mod 内的 MapExtend（三种布局，优先推荐 EC2BUnofficialPatch/MapExtend）；本地调试：plugins/EC2BUnofficialPatch/MapExtend。两份扩展JSON和PNG外置，MapExtend/Cfg只生成MapCfg/BgCfg材料且不读取，仍需原版Mod注册。旧 WorldMaps 目录读取兼容；同一 Mod 存在 MapExtend 时优先新目录。旧 JSON schema 不在本次变更范围。
- 工具：原版优先级2、新增默认3；无功能ID/行动基数/鹅城交通父地点/解锁绕过开关。新地图可选默认开放，父/子地点分别选首次开放。后备描边改色立即预览，编辑顶点与游戏轮廓分开。缩放百分比、适应窗口、100%、Ctrl滚轮、空格/中键平移只改变视图，坐标、尺寸及最终JSON不变。
- 存档使用独立 WorldMaps.v2.worlds/maps/actions 键的原生 FuncModel 容器，保存当前地图、开放状态、初始化地点与稳定行动映射；新游戏/读档清理静态状态。原版 CFG/资源须重启；F9 重读扩展JSON，重开地图应用。模板及完整游离测试包同步输出。


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

## 羽毛球模型贴图新增

- 配置位置为 Workshop Mod 的 `custombadminton/custombadmintoncfg.json`，推荐 `EC2BUnofficialPatch/custombadminton/custombadmintoncfg.json`，也接受其他容器下的 custombadminton；插件安装目录中的作者模板不参与运行时扫描。
- 玩家开关为 `[机制] 羽毛球模型贴图新增 = true`，默认开启。
- 配置根对象按人物 ID 映射。对手使用 `"9001": { "id": 9001, "outfits": [{ "url": "custom_role_9001", "prefab": "badminton_enermy1" }] }`；每套球服独立指定 url 与要复用的原版 prefab 名称，不填写 prefab 路径和扩展名。
- 同一对手 ID 的有效外置球服跨 Mod 追加。每次打开羽毛球小游戏时，在该 ID 的原版球服（若有）与全部有效外置球服中等概率选一套；无追加的原版角色仍始终使用原版，新 Mod 角色只有一套有效球服时始终使用该套。不同来源的重复 url 按扫描顺序保留先加载项。
- 旧对手配置的 `url` 数组仍只取首个非空 url，并与新增球服一起参与选择；旧格式的同一 ID 也可跨 Mod 追加。`id=0` 玩家保持旧配置和行为：男性使用 `url[0]`，女性使用 `url[1]`，只有一项时男女共用，重复玩家登记仍只保留先加载项；玩家不随机换装。
- 每个 url 必须有同名子目录，包含 `<url>_ske.json`、`<url>_tex.json`，以及 tex 的 `imagePath` 指定的 PNG。所有路径必须留在该 url 子目录内。
- 不读取外置 `.mat`。UP 克隆模板 prefab 的普通材质与 UI 材质，并将其主纹理替换为外置 PNG，因此各角色实例不会改写模板共享材质。
- 外置 ske 必须包含模板 `UnityArmatureComponent.armatureName` 指定的 Armature；羽毛球现有模板使用 `Armature`。ske 的显示部件名、tex 的 `SubTexture` 名和动画名必须相互匹配；玩法仍要求 `idle/hit/hit2/hit3/jump/move/start` 等原动画协议。
- 敌方 prefab 必须保留 `anim_enermy/Armature`，玩家 prefab 必须保留 `anim_player/Armature`，Animator、`AnimationEvt`、trigger 与碰撞节点继续由模板提供。
- 无效资源在登记时逐套跳过；实例化时骨骼或动画不兼容会记录错误并恢复模板角色。
- F9 会重建注册表；新资源在下一次实例化该角色时应用。只修改 PNG 后也可按 F9，因为该提供者每次都会重新读取文件时间与长度。

## 外部屏幕资源

### `5001`：ScreenPaper

- 文件搜索位置：内容根目录的 `EC2BUnofficialPatch/ScreenPaper/Custompaper.json` 或 `ScreenPaper/Custompaper.json`，也支持其他容器下的 ScreenPaper。
- 格式：`{ "papers": [{ "id": 正整数, "image": "相对图片路径" }] }`。
- `id` 必须已存在于最终原版 `PaperCfg`；插件只覆盖图片，不注册纸条文本。
- 图片必须位于同一 ScreenPaper 目录内，仅支持 PNG/JPG/JPEG；禁止绝对路径和用 `..` 逃离目录。
- 多个 Mod 声明同一 ID 时，该 ID 的自定义覆盖全部禁用并回退原版。

### `1164`：ScreenVideo

- 文件搜索位置：内容根目录的 `EC2BUnofficialPatch/ScreenVideo/CustomVideo.json` 或 `ScreenVideo/CustomVideo.json`，也支持其他容器下的 ScreenVideo。
- 格式：`{ "videos": [{ "id": 正整数, "name": "备注", "video": "相对视频路径", "volume": 0~1, "loop": bool, "skippable": bool, "roleOnTop": bool, "scale": "fit|fill|stretch" }] }`。
- EFFECT：`1164,1,id` 播放；`1164,0` 停止。正常剧情由 `CommonEvtMgr.GenEffector` Prefix 截获，返回 `EffectorScreenVideo`；编辑器预览参照 BA 的预览入口，在 `PreviewTalkView` 的有字/空文本对话中直接执行。其余 EFFECT 不受影响。
- 视频必须位于同一 ScreenVideo 目录内，仅支持 mp4/webm/mov/m4v；禁止绝对路径和 `..`。重复 id 以先加载者为准。
- 播放层是独立的置顶 Canvas + VideoPlayer（URL 源，Direct 音频输出），同一时间只播放一个；CG 流程中完整覆盖视频位于 CG 之上，debug 控制台仍在最上层；回到主菜单自动收起。`roleOnTop=true` 时画面节点在普通对话中挂在 `group_role` 前，在 CG 流程中移到 CG 图像与 `mask_talk_cg` 文本层之间，CG 收起后移回；编辑器预览关闭时停止其视频。首帧淡入和正常退出淡出各约 0.22 秒，淡出结束才释放视频占用的原版音乐通道。
- `roleOnTop=true` 优先按动态背景处理，允许正常操作对话，即使同时声明 `skippable=true` 也不跳过。否则视频阻挡界面操作，`skippable=true` 时长按左键约一秒可跳过。旧 `block` 字段不再参与判断。
- 播放前暂停原版音乐通道；结束、跳过、替换和显式停止都会释放视频的占用。BA 音源独立播放，与视频共用原版音乐的优先级管理。

### `5002`：ScreenLyrics

- 歌词项必须有正整数 `id` 与非空 `text`；`text` 中的 `\\n` 会转换为换行。
- `audio` 可省略；若填写，必须是正整数且对应可用音乐 ID（包括 BetterAudio 注册的可用路径）。
- `audio` 先按原版 `AudioCfg` ID 解析，再按已启用 BA 的 ID 解析。播放自身音频时暂停原版音乐并暂借 BA 当前播放会话；歌词结束后先恢复 BA，再按视频、BA、5002 的共同占用状态决定是否恢复原版音乐。
- 多个来源声明同一歌词 ID 时，该 ID 标记冲突，不选择“后加载者覆盖前加载者”。

## Live2D 与静态立绘

- 原版使用 Cubism：人物配置含 `l2d`/`l2d2` 与 `l2dParm`/`l2dParm2`，常规加载目标为 `l2dmodels/<name>/<name>` 的 Addressables prefab。
- 该 prefab 依赖 Cubism 组件、遮罩纹理、动作与动画控制器；普通 PNG 不能直接作为外部 Live2D 模型。
- 1.0.21 的外置 L2D 只用于地图社交界面中的静态 Mod 角色，可驱动标准参数的待机、眨眼、呼吸和鼠标跟随，并读取有效 `physics3.json`；不读取 motion3、表情或 Animator Controller，也不接入 Talk。

### 地图社交界面外置 Live2D

- 玩家开关：`[机制] 地图社交界面外置Live2D = true`，默认开启。
- 配置位置：推荐 Workshop Mod 的 `EC2BUnofficialPatch/ExternalLive2D/ExternalLive2D.json`，也支持直放或其他容器下的 ExternalLive2D。游戏插件安装目录中的作者模板不作为运行时配置读取。
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


## 1.0.21 合并补充（含 1.0.24/1.0.25）

外部小游戏 Context 的 Begin/Cancel/失效事件与 deferStart 参数见 ../../Docs/MINIGAMES.md。独立 NPC 键与阶段编号不变。

自动更新沿用 product=StudentAge.BA.UP/schema=1，并用 `layout=merged` 匹配当前单 DLL 安装布局；详见 ../../Docs/AUTO_UPDATE.md。

## MapExtend 归并与场景对象

MapExtend 场景对象（2026-10-10）
SceneObjectscfg.json 新制作内容使用 schema=2，objects 数组。每项包含自动生成且不在 UI 展示的 key、作者填写的 name、所属 mapId、PersonCfg 的 personId、绑定实际背景的 bgId。
normalCondition 与 clickCondition 是独立的原版条件二维数组，空数组代表默认满足。前者只控制普通图片显示；后者控制点击和悬停，不会隐藏本来可显示的普通图。无普通图时可用多边形区域点击背景里已有的物件。
UP新增物件介绍保留当前背景与本场景应显示的普通图，收起悬停图；在家保留原版模糊，地图场景复用已显示背景精灵，不另载BgCfg原图。普通图仍按 normalCondition 与100,11独立覆写判断。关闭介绍恢复原场景交互，不修改图片配置、背景登记或命中区域；原版物件介绍不改写。
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

## 地图编辑表单与自动开放（2026-10-09）

- 新大地图 ID 必须作者填写，范围 1～16777215、不可重复；可选名称留空使用新区域<ID>。父地点 ID 不再自动分配。
- 第二步只显示自动开放的父地点，第三步勾选与 ID 文本即时双向同步，子地点清单不会因编辑父地点列表而丢失。交通入口勾选会补勾父地点自动开放，反向不设置交通入口；交通入口自动开放不能取消，清单也强制保留对应 ID；取消交通标记后恢复自由选择。
- 子地点生成器取消类型选择，固定 type=2。校验 Math.floor(childId/1000)===parentId，范围父ID×1000～父ID×1000+999，最大 int32；禁止占用 ID。错误时不提交待保存 CFG，也不先生成/覆盖父地点。原版证据：[MapSceneView.RefreshFloor](D:/codex/assembly_ref/Assembly-CSharp/View/Main/MapSceneView.cs:400)，已内部对照2026-09-25受管快照。旧 Mod 的显式 floors 关系仍保留兼容。
- PreparePlayer 合并显式 autoUnlockMaps 与交通父地点，不追加入口 floors。已有存档 InitializedMaps 与原版地点进度不清空；原来的只初始化一次语义不变。子地点如需同时开放，应显式补到清单。CFG/JSON schema 与资源相对路径不变，模板生成位置与写出流程保持。

制作建议：建议子地点尾号从 001 开始，不建议直接使用 000 尾号。000 虽满足 ID 整除规则，工具仍允许保存，但不作为推荐范例。


## 2026-10-11 场景编辑隔离修复

场景按 MapId + BgId 分开保存和显示；当前场景的所有对象选区与图片同时保留，切换场景不会迁移对象。已保存场景可来回切换，导出后重开仍按同一归属恢复。填写或修改 MapId/BgId 后必须点击“确认场景（创建 / 切换）”，输入过程不创建场景、不切换当前编辑范围；确认的空场景也随模组保存。可在已保存场景选框下删除当前场景，含对象、参考图或自定义尺寸时再次确认；删除对象、选区、参考图及独占图片/人物行，共享内容保留；需要主动迁移对象或校正旧版误存的归属时，在“对象”页使用“调整此对象的所属场景”，图片位置、尺寸和选区保持不变。
图片与参考图异步载入绑定启动时的对象或场景，后到的旧请求不能写入新选择；删除对象或重开项目会作废对应的载入请求。切换清除拖动、悬停、顶点及文件输入状态。对象列表只列出当前场景对象；空场景清空对象表单，删除最后一个对象不会跳至其他场景。

## 2026-10-11 场景确认、删除与物件效果


场景物件效果：100,11,1,PersonId 显示普通图；100,11,-1,PersonId 隐藏普通图；100,11,2,PersonId 允许点击及悬停；100,11,-2,PersonId 禁止点击及悬停。
这些指令独立于普通图显示条件/可点击条件，不改写条件。未执行指令时遵循条件；执行后相应方面以最后一次明确指令为准，显示和点击状态互不影响，并随存档保存。仍限于对象所属地点及背景。旧存档没有指令记录时继续按条件判断。
PersonId 必须已按原版注册，且在启用内容的 SceneObjectscfg.json 中能找到对象。显示/隐藏要求普通图；可点击/不可点击要求普通图或悬停图至少一张；没有所需图时不记入状态并提示日志。相同 PersonId 绑定的多个对象共享这一状态，需独立控制时使用不同人物编号。兼容运行时旧 schema=1 对象。
移动指令 100,1,MapId 已归入 MapExtendEffects/MapMoveService，复用原有父/子地点与回家过渡；同时启用移动修复和地图扩展只注册一次。

WorldMapEffects.cs 已更名 MapExtendEffects.cs；移动服务源文件归入 MapExtend。100,1 的补丁与100,11的补丁分离：移动提供者不依赖另一程序集的场景状态，物件效果由实际加载的地图模块注册。重复提供者按 Harmony 前缀类名/方法名识别；地图模块检查已加载的 MapLocationsPatches.OpenPrefix，已有提供者时跳过重复场景/保存/F9注册。

独立MapExtend1.0.0与配套UP使用相同JSON协议、三种布局、相对图片路径和WorldMaps.v2存档键；两版嵌入相同制作工具，输出可互相直接导入/运行，不需要转换或额外Editor目录。模板均生成至BepInEx/plugins/EC2BUnofficialPatch/MapExtend；MapExtend/Cfg仍只是原版注册材料，须作者自行按原版流程注册。共存时UP地图优先，独立版不重复读取；UP地图关闭则由独立版接管，原版CFG来源与子地点关系交给实际提供者。此修改不新增或改写模组字段。
object-normal、object-click 为独立原版FuncModel容器，FuncSubData.isOpened保存明确true/false；无键代表没有覆写，Load/Clear清空旧会话状态。运行时刷新、悬停与实际点击使用同一覆写判定，背景归属仍是强制范围。
删除场景只清理编辑工程里的绑定内容，不删除原磁盘文件；共享PNG、人物行以及全局MapCfg/BgCfg不会作为该场景独占资料删除。
