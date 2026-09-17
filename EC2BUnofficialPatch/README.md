> 说明：以下为 UP 1.0.21 的完整功能说明；当前统一安装、音频内置、更新和构建方法见 [仓库首页](../README.md)。

# 一、简介

EC2BUnofficialPatch，一款适用于游戏《学生时代》，基于 BepInEx 和 Harmony 的**非官方兼容、修复与扩展补丁**。

插件面向玩家和 Modder，在尽量保持原版 CFG 与创意工坊 Mod 使用方式不变的前提下，修复部分原版机制问题，并为剧情演出、外置资源和 Mod 角色提供更多扩展能力。

当前主要包含：

- 4006 / 4016 / 4021 / 4022 / 5001 / 5002 等屏幕演出扩展，1164 播放 Mod 自带视频
    
- 3003 行动指令及部分原版 EFFECT 修复
    
- LoveDraw 情侣画外置图片、视频支持
    
- 社交小游戏扩展与自定义小游戏注册
    
- Mod 角色社交出现条件与考试资格控制

- 地图角色交互界面的静态 Mod 随机服装显示

- 地图角色交互界面的外置 Live2D（model3/moc3 直读，默认开启）
    
- 原版 / BetterAudio / Unity 三渠道音频播放监控
    
- 静态立绘与连续 CG 播放优化
    
- Mod CG 图鉴排序优化

- 游戏内 F9 热重载 Mod、UP 与兼容插件的 JSON 注册表

- 多个 Mod 可同时向同一父地图注册楼层

- 对话中的严格连续 `{1}{2}{3}` 可显示当前 Steam 昵称
    
- 普通考试手动输入成绩
    
- 每回合多次情侣话题
    
- 关系效果与关注人数机制修复

- 可选的游戏启动后自动打开游戏内调试控制台
    

当前版本：**1.0.21**
当前代码包对应游戏版本：**1.93**

_本项目主要用于补充原版及 Mod 开发中实际遇到的兼容性与扩展需求。_

# 二、安装方法

## 2.1 前置需求

- 《学生时代》游戏本体
    
- BepInEx 5.x
    
- 建议启用 BepInEx 控制台或日志文件
    

## 2.2 安装插件

获取编译后的：

```
EC2BUnofficialPatch.dll
```

放入：

```
<游戏目录>/BepInEx/plugins/
```

启动游戏即可。

首次运行后会生成配置文件：

```
BepInEx/config/sa.EC2B.UnofficialPatch.cfg
```

并在：

```
BepInEx/plugins/EC2BUnofficialPatch/
```

维护 Mod 作者参考模板。

绝大多数功能默认开启，不需要玩家额外操作。

## 2.3 自动更新
本插件自v1.0.17后支持自动更新，开启游戏后会自动检查更新，如可以更新，则会拉取最新版本并进行自动替换，但**注意需要重启游戏才可以完成更新**。

# 三、功能总览

|分类|功能|作用|
|---|---|---|
|屏幕特效|4006 黑屏文字扩展|黑屏过场可使用指定文本，而非固定原版文本|
|屏幕特效|4016 漫画扩展|支持 Workshop Mod 外置漫画图片|
|屏幕特效|4021 / 4022|扩展黑白、像素化等背景效果|
|屏幕特效|5001 屏幕纸条|为原版 PaperCfg 条目使用自定义图片|
|屏幕特效|1164 屏幕视频|用 EFFECT 播放 Mod 自带的视频文件，用法同 1163 音频|
|屏幕特效|5002 滚动歌词|自定义滚动文字，并可配合原版或 BetterAudio 音乐|
|行动指令|3003 修复|修正角色缩放指令的异常表现|
|EFFECT|36 动画修复与扩展|改善动画相关 EFFECT 与 Mod 动画内容兼容|
|EFFECT|100,1 地点移动|修复地点移动 EFFECT|
|EFFECT|20 关系效果|补全并修正角色离开、恢复等关系效果|
|机制|LoveDraw|支持情侣画外置图片、视频及增强版 CFG|
|机制|社交小游戏|支持角色独立阶段及自定义小游戏|
|机制|角色可用性|按原版 Condition 控制角色何时可社交、是否考试|
|机制|地图静态 Mod 角色服装|普通地图显示角色本回合随机服装，学校地图保持校服；缺少差分时回退常服|
|机制|地图社交外置 Live2D|为静态 Mod 角色按当前服装读取外置 model3/moc3；失败时回退静态立绘，默认开启|
|机制|音频监控|记录原版、BetterAudio、Unity 三类音频播放|
|优化|静态立绘|减少换表情等情况下的立绘跳变|
|优化|CG|改善连续 CG 切换，并整理 Mod CG 图鉴顺序|
|优化|.json 文件热重载|游戏内按 F9 重载普通 Mod CFG、UP JSON、BetterAudio.json 与已注册插件 JSON|
|优化|多mod地图子地点兼容|合并多个 Mod 在父地图中声明的子地点，未声明项按原版规则注册|
|优化|地图子地点扩展|允许子地点继续包含下一层子地点；需要 Mod 作者与玩家同时启用，默认关闭|
|优化|Steam昵称对话替换|将对话中严格连续的 `{1}{2}{3}` 整体替换为当前 Steam 昵称|
|优化|普通考试|可直接输入成绩并跳过普通考试小游戏|
|优化|情侣话题|支持一回合进行多次情侣话题|
|优化|关注人数|修复 `searchFriendCnt` 等统计异常|
|优化|游戏内调试控制台|配置开启后在游戏启动时自动打开 DebugView，默认关闭|

# 四、玩家常用功能

## 4.1 普通考试直接输入成绩

开启：

```
普通考试允许手动输入成绩 = true
```

进入普通考试时，可以选择：

```
手动输入成绩
```

或：

```
进入原版小游戏
```

输入成绩会自动限制在当前年级允许的 `0～maxScore` 范围内，小数自动取整。

**高考不受此功能影响。**

## 4.2 每回合多次情侣话题

配置：

```
[优化]
情侣话题每回合次数 = 1
```

例如：

```
情侣话题每回合次数 = 3
```

即可一回合最多进行三次情侣话题。

仅接受正整数，非法值自动使用安全值 `1`。

## 4.3 功能开关

插件功能均可在：

```
BepInEx/config/sa.EC2B.UnofficialPatch.cfg
```

单独关闭。

例如：

```
[优化]
静态立绘优化 = true
静态立绘优化输出日志 = false
CG播放与图鉴排序优化 = true
.json文件热重载 = true
多mod地图子地点兼容 = true
地图子地点扩展 = false
Steam昵称对话替换 = true

[机制]
情侣画修复 = true
社交小游戏修复 = true
地图社交界面外置Live2D = true

[屏幕特效]
4016漫画显示扩展 = true
5001屏幕纸条扩展 = true
5002屏幕滚动歌词扩展 = true
1164屏幕视频扩展 = true
```

# 五、Mod 作者快速接入

插件启动后可直接参考：

```
BepInEx/plugins/EC2BUnofficialPatch/
```

源码仓库中同时提供：

```
ModAuthorTemplate/
Examples/
```

无需修改插件 DLL。

## 5.1 4016 外置漫画

Mod 内建立名为：

```
comic
```

的目录，并在其下建立漫画子目录。

例如：

```
<Mod>/
└─ EC2BUnofficialPatch/
   └─ comic/
      └─ cg_01/
         ├─ 1-1.png
         ├─ 1-2.png
         └─ 2-1.png
```

图片命名：

```
{图号}-{分镜号}.png
```

`CGCfg.urls` 推荐使用：

```
Mods/<packageId>/EC2BUnofficialPatch/comic/cg_01
```

其中 `packageId` 指 ModMetadata 中的 packageId，而不是 Steam Workshop 数字 ID。

## 5.2 5001 自定义纸条

纸条文字等内容仍使用原版：

```
PaperCfg.json
```

插件只负责替换图片。

目录例如：

```
<Mod>/EC2BUnofficialPatch/ScreenPaper/
```

其中：

```
Custompaper.json
paper_1.png
```

示例：

```
{
  "papers": [
    { "id": 1, "image": "paper_1.png" }
  ]
}
```

## 5.2.1 1164 屏幕视频

和 1163 导入音频一个思路：视频文件放在 Mod 里，JSON 登记 id，EFFECT 里按 id 调用。

目录：

```
<Mod>/EC2BUnofficialPatch/ScreenVideo/
  CustomVideo.json
  intro.mp4
```

CustomVideo.json：

```
{
  "videos": [
    { "id": 1, "name": "开场", "video": "intro.mp4", "volume": 1.0, "loop": false, "block": true, "skippable": true, "roleOnTop": false, "scale": "fit" }
  ]
}
```

EFFECT：

```
1164,1,x   播放 id=x 的视频
1164,0     停止当前视频
```

- `block=true`（默认）：视频盖住整个画面并拦截点击，播完（或点击/空格/回车/Esc 跳过，需 `skippable=true`）后剧情才继续；`block=false`：只作为覆盖层，对话照常推进，适合配合 `loop` 做动态背景，用 `1164,0` 收掉。
- `roleOnTop=true`：视频插到对话界面的背景和人物立绘之间，立绘、对话框显示在视频上面（需要当时正开着对话界面，否则按盖住播放）；默认 false 视频盖住一切。
- `scale`：`fit` 等比留黑边、`fill` 等比裁切铺满、`stretch` 拉伸铺满。
- 非循环视频播完自动收起；回到主菜单/读档时也会收起。
- 视频建议 H.264 mp4，1080p 以内；支持 mp4/webm/mov/m4v，文件须在 ScreenVideo 目录内，不允许绝对路径或 `..`。
- 不同 Mod 的 id 请避免重复，重复时以先加载的为准。F9 热重载会重新读取 CustomVideo.json。

## 5.3 5002 滚动歌词

目录：

```
<Mod>/EC2BUnofficialPatch/ScreenLyrcis/
```

配置：

```
CustomScreenLyrcis.json
```

可以设置：

- 文本
    
- 字号
    
- 行距
    
- 颜色
    
- 对齐
    
- 可选音乐
    

音乐可使用原版音乐 ID，也可以使用 BetterAudio 已注册的音乐 ID。

## 5.4 LoveDraw 外置资源

支持：

```
PNG / JPG / JPEG
MP4 / WEBM / MOV / M4V / OGV
```

推荐：

```
<Mod>/
└─ EC2BUnofficialPatch/
   └─ LoveDraw/
      ├─ LoveDrawCfg.json
      └─ paint/
         ├─ example.png
         └─ example.mp4
```

CFG 仍保持原版字段：

```
"img": "paint/example.png",
"video": "paint/example.mp4"
```

插件同时支持“无插件兼容版 CFG + EC2B 增强版 CFG”，不会修改或覆盖 Mod 原文件。

## 5.5 社交小游戏

原版仍使用：

```
PersonGrowCfg
MinigameCfg
MinigameActionCfg
```

插件额外支持：

```
CustomMinigamecfg.json
```

四种后备实现：

|   |   |
|---|---|
|type|用途|
|`direct`|直接使用原版小游戏|
|`alias`|自定义 ID 复用某个原版玩法|
|`dialogue`|纯剧情阶段|
|`external`|调用外部 DLL 自定义小游戏|

并为不同 NPC 分别保存社交小游戏阶段，避免多个角色之间互相串进度。

## 5.6 控制角色何时出现

创建：

```
<Mod>/EC2BUnofficialPatch/RoleAvailabilityCfg.json
```

例如：

```
{
  "roles": [
    {
      "personId": 103,
      "cond": [[2, 110, 2007, 9]],
      "takeExam": true
    }
  ]
}
```

`cond` 直接使用原版 Condition。

条件满足前，该角色不会进入对应社交入口；同时可通过 `takeExam` 决定其是否参加考试。

该功能**不会删除角色，也不会修改原版 CFG 或存档中的角色定义**。

## 5.7 地图子地点兼容与扩展

`多mod地图子地点兼容` 默认开启。它将多个Mod在父地图中声明的子地点合并；没有显式声明归属的 `type == 2` 地图继续按照原版 `子地图ID / 1000` 规则注册。

父地图 `MapCfg.floors` 是权威归属声明，可以填写任意已存在的地图ID，不要求子地图ID能够除以1000得到父地图ID。例如父地图 `2` 可以直接声明子地图 `9001`：

```json
"2": {
  "floors": [2, 9001]
}
```

UP会为显式 `floors` 建立反向索引，使进入 `9001` 后仍能返回父地图 `2` 的楼层栏。只有完全没有被任何父地图声明的 `type == 2` 地图，才会使用原版 `子地图ID / 1000` 方式兜底推断。

多个Mod修改同一父地图时，父地图的名称、背景、条件等普通字段仍由原版优先级最高者决定；`floors` 则汇总所有有效声明并去重。旧Mod中复制父地图再添加楼层的写法仍然兼容。

`地图子地点扩展` 默认关闭。开启后，`type == 2` 地图才可以继续声明下一层。列表中的自身ID是本层入口，已确认的祖先ID是返回入口，其余有效ID才建立为直接子地图：

```json
"101":       { "type": 0, "floors": [101, 1470801] },
"1470801":   { "type": 2, "floors": [101, 1470801, 9001, 9002] },
"9001":      { "type": 2, "floors": [] },
"9002":      { "type": 2, "floors": [] }
```

进入 `1470801` 时显示其自身层级，进入 `9001` 或 `9002` 时显示 `1470801` 的楼层列表。循环关系会被忽略；同一地图被多个父地图声明时保留先建立的父关系。此扩展必须由Mod作者和玩家同时启用；未启用UP或关闭该开关时不能可靠使用这种分层结构。

## 5.8 Steam 昵称对话替换

对话文本严格写成：

```text
{1}{2}{3}
```

三个占位符连续且顺序完全一致时，整体显示为当前 Steam 昵称。`{3}{2}{1}`、`{1}{2}`、`{1} {2}{3}` 和全角括号写法都不会触发，仍按原版规则处理。

## 5.9 地图社交界面外置 Live2D

此功能默认开启，只替换地图角色交互界面（`MapRoleView`）中原本显示静态立绘的 Mod 角色；不接入 Talk，不替换原版 Live2D。某套服装没有登记、资源不完整或运行时加载失败时，会保留/恢复该服装的静态立绘。

### 目录与最小资源

每个 Mod 使用以下目录：

```text
<Mod>/EC2BUnofficialPatch/ExternalLive2D/
├─ ExternalLive2D.json
└─ 1001/
   └─ cloth0/
      ├─ model.model3.json
      ├─ model.moc3
      ├─ texture_00.png
      └─ model.physics3.json     （可选）
```

运行时最少需要 `model3.json`、它引用的 `moc3` 和全部 PNG 纹理；`physics3.json` 可选。`cmo3` 是编辑源文件，不能替代 `moc3`。当前版本不读取 `motion3.json`、`exp3.json` 或 Animator Controller。不要随 Mod 分发 `Live2D.Cubism.dll` 或 `Live2DCubismCore.dll`，插件使用游戏自带运行时。

所有路径都相对 `ExternalLive2D.json`，并且 `model3.json` 内部引用也必须留在该模型目录树中；禁止绝对路径和 `..`。纹理只支持 PNG。

### 登记模型

在 `ExternalLive2D.json` 的 `models` 数组中按 `personId + clothId + schoolStage` 登记：

```json
{
  "models": [
    {
      "personId": 1001,
      "clothId": 0,
      "schoolStage": "primary",
      "model": "1001/cloth0/primary/model.model3.json",
      "fitScale": 1.0,
      "xOffset": 0,
      "yOffset": 0,
      "staticAnchorX": 0.5,
      "staticAnchorY": 0.5,
      "modelAnchorX": 0.5,
      "modelAnchorY": 0.5,
      "flip": false
    }
  ]
}
```

`clothId` 必须对应游戏这次实际请求的服装编号。`schoolStage` 可填 `primary`（小学）、`middle`（中学）或 `all`（共用）；省略等同 `all`。同一个 `personId + clothId` 可以各登记一条 `primary` 和 `middle`，查找时优先当前学段，再使用 `all` 回退。游戏 `GradeState=0` 对应小学和 `url/urlParm`，`GradeState>0` 对应中学和 `url2/urlParm2`。多个来源只有在三项完全相同的时候才构成冲突。

### 自动对齐原静态立绘

插件先读取同一服装的静态立绘以及已由 `PersonCfg.urlParm/urlParm2` 施加的缩放和 XY 偏移，因此不要把 `urlParm2` 的数字再次抄进 Live2D 配置。随后排除静态 PNG 的透明留白，以可见像素范围和模型当前可见 Drawable 范围换算：

```text
基础模型倍率 = 静态立绘可见像素的实际显示高度 / 模型可见 Drawable 高度
最终模型倍率 = 基础模型倍率 × fitScale
最终位置 = 静态立绘锚点 - 模型锚点 + (xOffset, yOffset)
```

`fitScale=1` 表示可见高度等高；大于 1 放大，小于 1 缩小。`xOffset` 正数向右、`yOffset` 正数向上，单位为 UI 像素。四个锚点字段均为 0～1 的归一化坐标：X 的 0/0.5/1 分别是左/中/右，Y 的 0/0.5/1 分别是下/中/上。默认全部为 0.5，即双方可见边界中心对中心。

画布分辨率、透明留白和人物在画布中的相对位置可以不同；默认算法仍会自动换算。若静态图和模型的姿势或长发摆幅不同，整体外接框的中心不一定代表同一个人体位置，此时应选择双方相同的语义点作为锚点，例如头顶、下巴或腰线：先只调 `fitScale`，再调四个锚点，最后才用 `xOffset/yOffset` 做少量像素修正。锚点与偏移均按单个服装登记，不会影响该角色其他服装。

建议校准顺序：

1. 保留同一服装的静态立绘注册，先用默认 `fitScale=1` 和四个 0.5 锚点进入地图角色界面。
2. 只比较人物可见高度；太小就提高 `fitScale`，太大就降低。
3. 若身高一致但头、脸或腰线不重合，调整 `staticAnchorX/Y` 与 `modelAnchorX/Y`，让双方锚点代表同一人体位置。
4. 仅在最后用 `xOffset/yOffset` 消除几像素到几十像素的剩余误差。
5. 分别验证睁眼、闭眼、鼠标移到四角和重新打开界面；确认遮罩、眼球、物理和复用均正常。

若以发卡中心为锚点：在 Photoshop 用人物非透明选区取得可见边界 `L/T/R/B`，用参考线读取发卡中心 `Px/Py`；计算 `AnchorX=(Px-L)/(R-L)`、`AnchorY=(B-Py)/(B-T)`。在 Cubism 将参数恢复默认、导出透明背景默认姿势 PNG，再在 Photoshop 对同一发卡中心重复计算，分别写入 `staticAnchorX/Y` 和 `modelAnchorX/Y`。建议保留 3～4 位小数，游戏内每次以 0.005～0.02 微调锚点，最后才使用像素偏移。

若静态 Sprite 因打包方式不可读取像素，插件会安全退回完整 Rect 对齐；此时锚点和偏移仍然有效。旧版 `scale/x/y` 继续作为 Cubism 本地绝对倍率和绝对坐标兼容，但不建议新配置使用；旧字段不能与 `fitScale/xOffset/yOffset` 或锚点字段混用。

### 动作、遮罩与性能

即使没有 `motion3.json`，只要模型含有对应标准参数，插件也会驱动：`ParamAngleX/Y/Z` 头部待机和鼠标跟随、`ParamEyeBallX/Y` 眼球跟随、`ParamEyeLOpen/ParamEyeROpen` 周期眨眼、`ParamBodyAngleX/Z` 与 `ParamBreath` 轻微待机，以及有效 `physics3.json` 的物理输出。缺失的参数只会跳过，不会阻止加载。

遮罩接入游戏自带的 Cubism 遮罩资源与绘制链。Mod 作者必须在 Cubism Editor 中正确设置剪贴 ID，并在导出后整套更新 `model3.json`、`moc3` 和纹理；若编辑器中正确而游戏中错误，请确认没有混入旧 DLL 或不匹配的模型文件，再查看首次成功日志中的 `masks` 与 `maskTexture`。

首次加载的 PNG 通过 Unity 异步纹理通道读取和解码，准备期间继续显示静态立绘；同一次构建会复用已读的 model3、moc3 和 physics3。相同模型在同一地图角色界面实例中会保留并复用，关闭后再次打开不重新解析 moc3 或解码纹理。静态图透明边界也按 Sprite 缓存，只在首次需要时计算。

每个模型在一次游戏进程中仅在“资源正确载入且游戏内成功显示”后输出一次成功日志；再次打开不重复输出。加载失败、配置冲突和回退仍会记录，以免隐藏真实故障。

### 玩家启用与排错流程

1. 安装 DLL 后启动一次游戏，在 BepInEx 配置中确认 `[机制] 地图社交界面外置Live2D = true`（默认就是 true）。
2. 将上述目录放进 Workshop Mod 根目录，确认 `ExternalLive2D.json` 是合法 JSON，路径大小写和文件名一致。
3. 进入地图并打开对应角色、对应服装。模型准备完成前短暂显示静态立绘，成功后自动切换。
4. 完全不显示：核对 `personId/clothId`、模型相对路径、moc3 版本和全部纹理；查找“外置Live2D加载失败”日志。
5. 显示但不动：核对标准参数 ID；自定义参数名不会被自动驱动。物理还需要有效 `physics3.json` 及正确输入/输出参数。
6. 眨眼时瞳孔漏出：在 Cubism Editor 检查瞳孔 Drawable 是否被眼白/眼睑的正确 ArtMesh 剪贴，重新导出全部运行时文件，并避免只替换 moc3 而保留不匹配的 model3/纹理。
7. 位置不一致：按“先比例、再锚点、后偏移”的顺序校准，不要直接套用另一分辨率素材的数值。

# 六、音频播放监控

插件可以独立监控：

```
原版音频渠道
BetterAudio音频渠道
Unity底层音频渠道
```

并在 BepInEx 日志中输出能够解析到的音频名称、资源键或文件路径。

适合：

- 查找原版 BGM / 音效
    
- 制作剧情 Mod
    
- 排查音频冲突
    
- 分析某段剧情实际调用的声音资源
    

三个渠道默认关闭，可按需分别开启；启用后会输出较多诊断日志。

# 七、CG 与立绘优化

## 7.1 CG

连续剧情 CG 会使用额外过渡处理，减少切换时短暂露出背景的问题。

对于 Mod CG 图鉴：

- 官方分组保持原版顺序；
    
- Mod 使用的 Group 3 按 CG ID 排序；
    
- 显示编号从 `001` 连续生成；
    
- 一个 `CGCfg` 仍视为一个图鉴条目。
    

## 7.2 静态立绘

`静态立绘优化` 默认开启；`静态立绘优化输出日志` 默认关闭。关闭日志不会关闭静态立绘优化，只停止该模块的加载、切换、回退和诊断输出。

改善静态角色在：

- 表情切换
    
- 贴图切换
    
- 外置立绘切换
    

等情况下出现的明显跳变。

无需 Mod 作者增加额外配置。

# 八、构建方法

目标框架：

```
net472
```

使用：

```
.\build.ps1 "你的 StudentAge 游戏目录"
```

或：

```
dotnet build EC2BUnofficialPatch.csproj -c Release /p:GameDir="你的游戏目录"
```

输出：

```
bin/Release/net472/EC2BUnofficialPatch.dll
```

游戏本体 DLL、BepInEx DLL 等依赖均从本地游戏目录读取，不随源码仓库分发。

# 九、常见问题

## Q：普通玩家需要配置这些 JSON 吗？

不需要。

JSON、模板和 Examples 主要供 Mod 作者使用。普通玩家安装 DLL 后即可使用修复和优化功能。

## Q：某个功能不想使用怎么办？

在：

```
BepInEx/config/sa.EC2B.UnofficialPatch.cfg
```

关闭对应功能即可。

## Q：安装插件后，原来的 Mod 会失效吗？

本项目尽量保持原版 CFG 结构与无插件兼容版本，并对 LoveDraw、Minigame 等内容采用运行时增强方式。

由于属于非官方运行时补丁，仍不能保证与所有修改相同游戏方法的其他插件同时兼容。

## Q：为什么某个 Mod 角色没有参加考试？

若该角色受到 `RoleAvailabilityCfg.json` 控制，请检查：

- Condition 是否满足；
    
- `takeExam` 是否为 `true`；
    
- Person / PersonGrow / ExamRank / Classmate 等考试数据是否完整。
    

不完整或明显错误的考试角色会被安全排除。

## Q：为什么没有自动更新？

自动更新功能于1.0.17版本加入，若下载的版本在此之前，是无法自动更新的。
除此之外，网络波动/github限流等因素也可能导致更新无法完成。
在完成更新后，需要重启游戏来完成.dll文件的替换。

# 十、项目地址与反馈

项目地址：

```
https://github.com/lfwing/StudentAge-EC2BUnofficialPatch
```

反馈问题时，建议提供：

- EC2BUnofficialPatch 版本
    
- 游戏版本
    
- BepInEx 版本
    
- 完整运行日志
    
- 涉及的 CFG / JSON
    
- 是否安装其他修改相同机制的插件
    

# 十一、许可证与免责声明

参考：
https://github.com/white12666/StudentAgeEditorPlus
在此提出感谢。

本 mod 基于 AGPL-3.0 协议开源，这是一份强 Copyleft（传染性）协议。通俗概括如下：
你可以自由地： 使用、修改本 mod，以及基于本 mod 的代码进行二次开发。
但你必须遵守：
若你复制、修改本 mod 的代码，或将其代码用于你的项目，在分发你的作品时，必须同样以 AGPL-3.0 协议开源，并提供完整源代码；
即使不公开分发文件，若你将修改后的版本部署在服务器上供玩家使用，也必须向这些玩家提供修改后的源代码；
保留本 mod 的版权声明与协议文本。
关于游戏本体： 本 mod 未包含、修改或分发游戏本体的任何代码与文件，仅通过 Harmony 运行时补丁与反射调用同游戏交互。
以上为通俗概括，具体权利义务以 [AGPL-3.0 协议原文] 为准。

---
附加许可（Additional Permission，基于 AGPL-3.0 第 7 条）
作为本项目的创作者，本人在 AGPL-3.0 协议之外，额外授予白雨工作室及其工作人员（仅限用于该工作室的开发与运营工作）一份免费、非独占、不可撤销的许可：
允许其以任何形式（包括但不限于闭源、并入游戏本体、商业用途）复制、修改、引用本项目中由本人创作的代码，不受 AGPL-3.0 各项义务（包括开源与源代码提供义务）的约束。
范围限定：
工作人员以个人名义、非为该工作室工作目的使用本项目代码时，不适用本附加许可，仍受 AGPL-3.0 约束；
依据 AGPL-3.0 第 7 条，任何再分发者可以选择移除本附加许可文本，但这不影响白雨工作室已获得的权利。
当前源码包中未声明明确的开源许可证。正式公开发布前，应由项目作者补充实际采用的许可证文本。

---
免责声明：
1. 本项目是基于 BepInEx 和 Harmony 的非官方同人 Mod，与游戏官方无关。
2. 插件通过运行时补丁扩展游戏行为，游戏更新后可能暂时失效。
3. 音乐、歌词和其他资源的发布者应确保自己拥有合法使用和分发权限。
4. 不建议把无授权的商业音乐直接打包上传至公开创意工坊。
5. 使用第三方 Mod 存在一般性的兼容和稳定性风险，建议提前备份存档及相关文件。

# 十二、致谢

- BepInEx：Unity 游戏插件框架
    
- Harmony：运行时补丁框架
    
- Newtonsoft.Json：JSON 配置读取
    
- BetterAudio：音频演出兼容
    
- 所有参与测试、制作 Mod 与反馈问题的玩家和 Mod 作者
