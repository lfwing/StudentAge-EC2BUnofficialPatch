# EC2BUnofficialPatch 1.0.16.2

Student Age / EC2B 的 BepInEx 非官方兼容与扩展补丁。

## 配置

首次运行生成：

```text
BepInEx/config/sa.EC2B.UnofficialPatch.cfg
```

所有布尔开关默认 `true`，次数项默认 `1`：

```ini
[优化]
静态立绘优化 = true
CG播放与图鉴排序优化 = true
普通考试允许手动输入成绩 = true
情侣话题每回合次数 = 1
关注人数统计优化 = true

[屏幕特效]
4006黑屏特效显示文字扩展 = true
4016漫画显示扩展 = true
屏幕特效扩展 = true
5001屏幕纸条扩展 = true
5002屏幕滚动歌词扩展 = true

[效果]
36动画相关修复与扩展 = true
100,1地点移动修复 = true
20关系效果修复与扩展 = true

[机制]
情侣画修复 = true
社交小游戏修复 = true
监控原版音频渠道 = true
监控BetterAudio音频渠道 = true
监控unity底层音频渠道 = true
控制角色在列表显示 = true

[行动指令]
3003修复 = true
```

## 1.0.16 新功能

- 普通考试（小游戏 ID 2）可选择输入最终总分或进入原版小游戏；小数四舍五入，分数按当前 `GradeCfg.maxScore` 截断。高考（ID 1）完全不拦截。
- `情侣话题每回合次数` 只接受正整数，默认 `1`；每次完成后会移除已选话题，并通过原版 EvtType 22 条件/历史/maxcount 规则补足最多三个候选。
- 修复 `searchFriendCnt` 长期错位与负数，并补全 `[20,520]`、`[20,-524]`、`[20,-525]`；520 与 521 的恢复范围严格分离。
- `RoleAvailabilityCfg.json` 使用原版 Condition 控制登记角色的社交入口和考试资格；不修改角色存在性，也不修改原版 CFG。

### RoleAvailabilityCfg.json

插件本地配置位置：`BepInEx/plugins/EC2BUnofficialPatch/RoleAvailabilityCfg.json`。
Workshop Mod 配置位置：`<Mod>/EC2BUnofficialPatch/RoleAvailabilityCfg.json`。

```json
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

`takeExam=false` 不要求考试配置；`takeExam=true` 必须有完整 Person、PersonGrow、当前学段 ExamRank 与对应 Classmate 数据，否则记录错误并从考试池安全排除。

### 音频播放监控

三个渠道完全独立：

- `原版音频渠道`：记录游戏 `Channel.Play/PlayOneShot`；能追溯时输出 `ResMgr` 资源键或外部文件路径。
- `BetterAudio渠道`：运行时检测 BetterAudio 程序集并监听其播放入口，不要求工程直接引用 BetterAudio DLL。
- `unity底层渠道`：监听 `AudioSource`，用于捕获绕过上层管理器的播放。

Addressables/AssetBundle 内的 AudioClip 不一定对应独立磁盘文件；此时日志会输出资源键，无法追溯时明确标记为 Unity 内存/包内资源。

## 4016 漫画

- 只扫描 Workshop Mod 中文件夹名为 `comic` 的目录，位置不限。
- 推荐：`<Mod>/EC2BUnofficialPatch/comic/<漫画名>/1-1.png`。
- 图片必须命名为 `{图号}-{分镜号}`，实际每页分镜数必须与 `CGCfg.comic` 完全一致。
- 推荐 `CGCfg.urls`：`Mods/<packageId>/EC2BUnofficialPatch/comic/<漫画名>`。
- `<packageId>` 是 `ModMetadata.packageId`（如 `hengwuyuan`），不是 Workshop 数字 ID。`Mods/<packageId>/` 是游戏逻辑前缀，原版会把它映射到对应 Workshop 根后丢弃此前缀；物理目录不需要真的存在 `Mods/<packageId>`。
- JSON 中旧式 `Mods\\pkg\\...` 与新版推荐的 `Mods/pkg/...` 等价。
- 详见 `Docs/ComicExternalResources.md`。

## 5001 纸条图片

- 纸条内容继续由原版 `PaperCfg.json` 注册，插件不新增或修改原版 CFG 字段。
- 在 `<Mod>/ScreenPaper/Custompaper.json` 或 `<Mod>/EC2BUnofficialPatch/ScreenPaper/Custompaper.json` 的 `papers` 数组声明 `{ "id": 1, "image": "paper_1.png" }`。
- `image` 只允许指向同一 `ScreenPaper` 目录内的 PNG/JPG/JPEG；缺图、坏图或未声明时使用原版图片。
- 未在原版/Mod `PaperCfg` 注册的 ID、越界路径与重复 ID 会报错；重复 ID 的所有自定义覆盖都会失效，避免跨 Mod 抢占。

## 5002 滚动歌词演出

- `CustomScreenLyrcis.json` 的每个歌词项可填写可选的 `audio` 音乐 ID；省略时也能按文字量和滚动距离动态演出。
- `audio` 可指向原版 `AudioCfg` 或 BetterAudio 注册的音乐 ID，音效类型 ID 不接受。
- 有 `audio` 时暂停原版音乐并播放演出音乐；任何 5002 演出都会暂停正在播放的 BetterAudio 音乐。
- 自然结束、关闭界面或长按左键跳过都会停止演出音频，并只恢复演出前实际在播放的原版/BetterAudio 音乐。

## LoveDraw / 社交小游戏的双版本 CFG

无插件兼容配置仍可放：

```text
<Mod>/Cfgs/zh-cn/LoveDrawCfg.json
<Mod>/Cfgs/zh-cn/MinigameActionCfg.json
```

插件增强版可分别放在：

```text
<Mod>/EC2BUnofficialPatch/LoveDraw/LoveDrawCfg.json
<Mod>/EC2BUnofficialPatch/Minigame/MinigameActionCfg.json
```

对应机制开启时，插件为**当前 Mod**建立运行时 CFG 视图：

- 两个版本都存在：增强版替代兼容版；
- 原版同名 CFG 不存在：增强版会被主动注入；
- 整个 `Cfgs/zh-cn` 不存在：增强版仍可正常进入原版 `LoadModCfgs -> cfgMaps -> MergeCfgsAsync` 流程；
- Workshop 原文件不会被复制回原目录、覆盖或删除，也不会跨 Mod 混用。

因此，兼容版 CFG 已经不是“插件用户正常加载”的硬性要求；它只决定**没有安装插件的玩家**能否获得对应的兼容内容。

## 外置资源路径

1.0.14 修订后，LoveDraw、Comic 等外置资源共用 `ExternalResourceResolver`。解析器现在明确区分 `ModMetadata.packageId`、Steam Workshop 数字 ID 和 Workshop 根内相对路径，并按原版 `ModCtrl.GetFullUrl()` 语义处理 `Mods/<packageId>/...`。多 Mod 出现同名资源时，推荐显式写 packageId；未限定来源时只接受唯一命中。

详见 `Docs/ExternalResourceRules.md`。

## 1.0.16.2 热修复

详见 `Docs/CHANGELOG_1.0.16.2.md`。

## 1.0.16.1 热修复

详见 `Docs/CHANGELOG_1.0.16.1.md`。

## 1.0.16 变更

详见 `Docs/CHANGELOG_1.0.16.md`。
