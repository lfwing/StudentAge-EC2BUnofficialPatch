# 一、简介

EC2BUnofficialPatch，一款适用于游戏《学生时代》，基于 BepInEx 和 Harmony 的**非官方兼容、修复与扩展补丁**。

插件面向玩家和 Modder，在尽量保持原版 CFG 与创意工坊 Mod 使用方式不变的前提下，修复部分原版机制问题，并为剧情演出、外置资源和 Mod 角色提供更多扩展能力。

当前主要包含：

- 4006 / 4016 / 4021 / 4022 / 5001 / 5002 等屏幕演出扩展
    
- 3003 行动指令及部分原版 EFFECT 修复
    
- LoveDraw 情侣画外置图片、视频支持
    
- 社交小游戏扩展与自定义小游戏注册
    
- Mod 角色社交出现条件与考试资格控制
    
- 原版 / BetterAudio / Unity 三渠道音频播放监控
    
- 静态立绘与连续 CG 播放优化
    
- Mod CG 图鉴排序优化
    
- 普通考试手动输入成绩
    
- 每回合多次情侣话题
    
- 关系效果与关注人数机制修复
    

当前版本：**1.0.16.2**  
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
|屏幕特效|5002 滚动歌词|自定义滚动文字，并可配合原版或 BetterAudio 音乐|
|行动指令|3003 修复|修正角色缩放指令的异常表现|
|EFFECT|36 动画修复与扩展|改善动画相关 EFFECT 与 Mod 动画内容兼容|
|EFFECT|100,1 地点移动|修复地点移动 EFFECT|
|EFFECT|20 关系效果|补全并修正角色离开、恢复等关系效果|
|机制|LoveDraw|支持情侣画外置图片、视频及增强版 CFG|
|机制|社交小游戏|支持角色独立阶段及自定义小游戏|
|机制|角色可用性|按原版 Condition 控制角色何时可社交、是否考试|
|机制|音频监控|记录原版、BetterAudio、Unity 三类音频播放|
|优化|静态立绘|减少换表情等情况下的立绘跳变|
|优化|CG|改善连续 CG 切换，并整理 Mod CG 图鉴顺序|
|优化|普通考试|可直接输入成绩并跳过普通考试小游戏|
|优化|情侣话题|支持一回合进行多次情侣话题|
|优化|关注人数|修复 `searchFriendCnt` 等统计异常|

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
CG播放与图鉴排序优化 = true

[机制]
情侣画修复 = true
社交小游戏修复 = true

[屏幕特效]
4016漫画显示扩展 = true
5001屏幕纸条扩展 = true
5002屏幕滚动歌词扩展 = true
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
    

三个渠道可分别关闭。

# 七、CG 与立绘优化

## 7.1 CG

连续剧情 CG 会使用额外过渡处理，减少切换时短暂露出背景的问题。

对于 Mod CG 图鉴：

- 官方分组保持原版顺序；
    
- Mod 使用的 Group 3 按 CG ID 排序；
    
- 显示编号从 `001` 连续生成；
    
- 一个 `CGCfg` 仍视为一个图鉴条目。
    

## 7.2 静态立绘

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