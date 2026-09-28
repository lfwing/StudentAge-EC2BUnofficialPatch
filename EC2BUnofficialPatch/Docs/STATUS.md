# 当前状态：UP 1.0.23 开发副本

2026-09-28 本轮改动的本地发布候选文件已生成：`dist/release/EC2BUnofficialPatch.dll` 为 586240 字节，SHA-256 `601086a74d41cb6f6700ba7da0a30adb9a93bd409bb1da9056f92f0bd37c1d91`；`dist/release/update.json`、`release-manifests/update.json` 与根目录 `update.json` 内容一致，版本 1.0.23、游戏版本 1.94，大小及哈希均匹配 DLL。构建为 0 错误、1 个既有未使用字段警告。仅制作本地候选，尚未发布或安装到游戏。

2026-09-28 本轮按限定范围实现三项改动，尚待游戏内复测。CG：`CGOptimization` 接管图鉴格子的缩略图请求，格子每次重绑递增代次并清除旧图；回调仅在格子仍显示同一 CG 时写入贴图，成功、空结果、路径错误和 20 秒超时都会结束当前转圈，翻页重试会发起新请求。此处理仅限图鉴缩略图，未修改全局 `UISprite` 或 `ResInfo` 行为；若原版 `ResInfo.GetSpriteAsync` 内部异常令共享请求永久停在 `isLoading`，需要后续实机日志再单独定位。配置键迁至 `[优化] CG播放与图鉴优化`，沿用旧键的值，说明同步加入分页缩略图修复。

2026-09-28 羽毛球：新增默认开启的 `[机制] 羽毛球模型贴图新增` 开关；对手 `custombadmintoncfg.json` 可用 `outfits` 数组逐套登记 `url/prefab`，同一 ID 跨 Mod 追加，重复 url 仅保留先加载的有效项，资源无效则逐套跳过。每次打开小游戏时，原版 `BadmintonModelCfg` 中存在该 ID 则将原版球服作为一项，连同全部有效外置球服等概率随机选择；新 Mod ID 只在有效外置项间选择。旧对手 `url` 数组仍只取首项作为一套；`id=0` 玩家保持既有配置和男女选择逻辑，不随机换装。实例加载失败沿用模板回退路径。作者模板 readme/JSON、数据格式和 README 已同步。

2026-09-28 作者模板：运行时生成的 `comic说明.txt` 改放 `comic/comic说明.txt`；启动时迁移旧根目录说明文件，模板说明文字也改为与新位置一致。构建使用游戏目录 `G:\SteamLibrary\steamapps\common\StudentAge` 的 `net472` 引用，0 错误、1 个既有未使用字段警告；未进行游戏内 CG 快速翻页及羽毛球概率/对象池复测。

2026-09-28 大地图新增地点（需求已记录，待设计/实现）：目标是让普通 Mod 声明全新的大地图地点，并自定义地点标签文字、颜色、位置与尺寸、地点图标、鼠标悬停描边区域及点击/跳转行为。现有“多 Mod 地图子地点兼容”和“地图子地点扩展”只处理已有地图层级的合并/嵌套，不等同于注册新的大地图地点。用户提供的 `D:\VS2026 project\studentages\StudentAgeMapModGenerator\StudentAgeMapModGenerator.zip`（标点与自动描边工具，并生成独立 BepInEx DLL）作为算法参考；后续评估改为 UP 共用运行时读取 Mod 数据/资源的注册流程，避免作者按地点单独生成 DLL。数据 schema、轮廓/命中区域、点击事件路由、跨 Mod 冲突与兼容策略尚未确定；本条仅记录需求，尚无实现或游戏内验证。

2026-09-28 羽毛球对手多套球服（原始需求记录；实现见上方，待实机验证）：仅影响对手，不修改玩家模型。原版 `BadmintonModelCfg` 角色始终保留为候选；`custombadminton` 中同一角色 ID 的有效 Mod 服装作为追加项。每次打开羽毛球小游戏时，在原版服装及所有追加服装中等概率随机选择一套。按等概率规则，无追加的原版角色始终为 100%；原版加 20 套 Mod 服装共 21 套，各为 1/21（约 4.76%）；只有一套的新 Mod 角色为 100%，有 10 套时各为 10%。此前外置角色模块是每个 ID 一条登记，重复 ID 会保留先加载项；普通 NPC 只取该条目的 `url[0]`，因此现有 `url` 数组不代表同角色随机换装。上述跨 Mod 追加、逐套 `url/prefab`、重复资源处理、随机选择及失败回退已按本轮记录实现；尚未实机验证。制作流程见 [羽毛球模型贴图制作指南](BADMINTON_MODEL_TEXTURE_GUIDE.md)。

2026-09-28 CG 图鉴分页缩略图偶发转圈（源码诊断；实现见上方，待实机验证）：实测翻页后同一缩略图有时正常、有时转圈，更符合“格子复用 + 异步回调竞争”，不能只归因于图片文件随机缺失。原版 `CGLibraryView.SetPage` 每页最多 9 项并复用 `UIItemGroup` 中已显示的 `UICell`；`UICell.SetData` 会重跑渲染，令同一个 `UISprite` 在前一页请求尚未完成时改绑到新 CG。`UISprite.SetTextureUrl` 的回调只绑定实例方法，不捕获/校验本次 URL 或绑定代次；旧请求完成可把旧 Sprite 写入当前格，空结果还会清掉新 URL。换 URL 时图片对象会先重新显示，而旧 Sprite 未清空。`ResInfo` 按路径缓存并合并进行中的请求，缓存命中可立即回调、冷加载异步完成，时序变化能解释间歇性。另有独立加载状态问题：原版 `OnRenderCG` 每次都启用 `root_loading`，C# 渲染路径没有对应完成关闭；因归档不含 prefab/Animator，若实测转圈有时自行消失，还需确认 prefab 是否另行控制。修复前 `CGOptimizationModule.LibraryOnRenderCgPostfix` 只改图号，不处理贴图加载。修复方向：为每次贴图请求及格子绑定加 URL/代次校验，过期回调不得写图或改加载状态；增加同时覆盖成功、失败/取消的完成回调，以结束转圈并显示占位/重试状态；换 URL 时清除旧图；`ResInfo.GetSpriteAsync` 加异常收尾，确保 `isLoading` 复位并通知等待者；核对 `cg_min/`、`cg2_min/` 对应资源 key。共享资源请求不宜因单格翻页而整体取消。证据为 `D:\codex\StudentAge-CodeOnly\assemblies\Assembly-CSharp\Assembly-CSharp.cs`（2026-09-25 快照）：`CGLibraryView.SetPage/OnRenderCG` 61638、61676，`UIItemGroup.SetData/UICell.SetData` 107456、108906，`UISprite.SetTextureUrl/OnLoadSpriteEnd` 108441、108523，`ResInfo.GetSpriteAsync` 105289；缩略图请求保护已按本轮记录实现；快速连续翻页复测尚未进行。

2026-09-28 CG 图鉴排序修复：在原有 `CGOptimizationModule` 内调整 Mod Group 3 的排序。`idx > 0` 的 CG 先按 idx 升序排列（相同 idx 按 ID 升序），其余 CG 再按 ID 升序排列；显示图号仍从 001 连续编号。以 2026-09-27 SHA-256 为 `3401d10d07ef789ad3965b607e23ff29882d3a0437318d4f0dc6b7db43d0ebc6` 的 release DLL 为基线；新候选 DLL 版本仍为 1.0.23，大小 580608 字节，SHA-256 为 `285a9fafec781f1b0846fc8f4272f9238cc0859f0419a6975be674b0ab73257e`。net472 编译通过，游戏内图鉴显示待复测。

2026-09-27 羽毛球外置角色实机修复：1.0.23 初版在同一 `Armature` GameObject 上释放并重建 DragonBones，旧 `UnitySlot` 的显示对象由 Unity 延迟销毁，新骨骼在当帧复用了这些对象，随后产生 `UnitySlot._UpdateTransform` 空引用。敌方骨骼的异常还会中断共享 `WorldClock`，使未替换的玩家小人也停止运动。现改为在全新 RectTransform 宿主中完整构建、校验全部插槽及七个玩法动画，成功后再切换 `anim_enermy/Armature` 或 `anim_player/Armature`，最后释放旧宿主；对象池恢复模板也使用相同替换流程。已通过 net472 编译，等待游戏内复测。

2026-09-27 羽毛球角色外置贴图：新增 `custombadminton/custombadmintoncfg.json` 注册表。每条登记沿用 `BadmintonModelCfg` 的 `id/url`，并以 `prefab` 指定复用的原版模板；运行时改载模板 prefab，再从 url 同名子目录读取 `_ske.json`、`_tex.json` 与 tex 中 `imagePath` 指定的 PNG，克隆模板材质并只重建当前实例的 DragonBones Armature。无需 Mod 作者提供 `.mat`。支持 NPC 和 `id=0` 玩家男女 url，F9 可重载登记和资源版本；失败时恢复模板角色。已通过 net472 编译，游戏内动画、对象池复用与多 Mod 冲突仍待实测。

2026-09-27 1.0.23 羽毛球实机修复候选构建完成：`dist/release/EC2BUnofficialPatch.dll` 大小 580608 字节，SHA-256 为 `3401d10d07ef789ad3965b607e23ff29882d3a0437318d4f0dc6b7db43d0ebc6`；根目录、`release-manifests/` 与 `dist/release/` 的 `update.json` 已同步为游戏 1.94、版本 1.0.23。编译为 0 错误、1 个既有未使用字段警告；尚未发布，修复后游戏内复测待完成。

2026-09-22 DialogueSave 0.1.5 兼容：本机订阅包 `3804899191` 与已安装 cfg（`DialogueStyle = ADV`）确认问题场景。其 [`AdvDialogueController`](https://github.com/360478712scy-gif/StudentAge-DialogueSave/blob/c40402ce621e47d3c27c8291e3265d996a0ae8d9/src/UI/AdvDialogueController.cs) 把 CG 实际文字移到排序 29000 的独立 `DialogueSave.ADV` Canvas，并把原 `mask_talk_cg` 移进透明 `ADV.NativeShield`；上一版将视频放在该 mask 的父节点，因而被透明遮罩隐藏。现在按文字是否位于 ADV Canvas 自动切换：ADV 时将视频层挂在遮罩外的原 CG 根下，排序在 CG 图片与 ADV 文字之间，不再改隐藏的原生文字 Canvas；切回原版/退出 CG 时恢复上一布局。播放期间 DebugCanvas 也会高于 DialogueSave 的模态画布。未修改 DialogueSave 或游戏文件，游戏内联动视觉效果仍待实测。

2026-09-22 CG 视频层级重做：移除上一版仅将 `roleOnTop=true` 视频插到 `mask_talk_cg` 前的方案；CG 子视图出现时建立临时视频 Canvas，按实际 CG 图片 Canvas 的排序层和值，将视频设在图片之上；原版文字模式下，再将 `mask_talk_cg` 排在视频之上。CG 退出或视频结束时销毁临时视频层并恢复文字 Canvas 原设置；Debug 控制台保持最上层。普通对话仍把动态背景置于角色后方。视频画面及底色保留与 CG 优化相同的 0.22 秒渐变节奏：首帧出现后淡入，正常播完、跳过、显式停止和替换时淡出，淡出完成才释放视频占用的原版音道；错误、准备失败和场景销毁立即清理。游戏内视觉效果仍待实测。

2026-09-22 CG 与编辑器预览：视频播放期间检测剧情 `CGView`（正常对话和编辑器预览的子视图），将完整覆盖视频画布调到当前 CG 画布上方，并让原版 `DebugCanvas` 始终高于视频；视频结束时恢复调试画布原层级。编辑器 `ModPreviewTipsView` 打开预览时仅加载当前 Mod 根目录的 `ScreenVideo` 注册表；`PreviewTalkView` 在有字对话的 `DoText` 和空文本对话的 `RefreshTalk` 执行 1164，预览关闭时停止其视频。动态背景和长按左键跳过也接受预览对话视图。未把游戏目录作者模板加入资源扫描。

2026-09-22 模板行为修正：恢复启动时在游戏 `BepInEx/plugins/EC2BUnofficialPatch` 创建 UP 作者模板；启用 BA 时也恢复在 `BepInEx/plugins/BetterAudio` 创建 BA 作者模板。仅创建供作者复制的文件，运行时 `ContentRootCatalog` 仍只纳入 Workshop Mod，不自动读取这两个模板目录作为资源。模板创建不覆盖现有 JSON，原有模板写入失败日志保留。

2026-09-21 后续优化：视频播放时将原版独立 `DebugCanvas` 临时提升到视频画布之上，结束后还原；若调试画布晚于视频创建，播放期间会继续检测。此举让控制台画面与输入位于视频遮挡层之上，不改变原版控制台快捷键逻辑。BA 启动自检成功时始终报告音频条目和资源包数量，视频模块启动时始终报告注册视频数量；两项日志开关现在只控制运行期间的信息日志，启动失败及资源错误仍始终输出。cfg 注释已同步。

2026-09-22 最新发布候选构建：按 `AGENTS.md` 指定的 `net472` / `GameDir` 流程构建通过（1 个原有未使用字段警告、0 错误），随后由 `build.py` 生成内置更新助手的单 DLL 和本地候选清单。发布 DLL 位于 `dist/release/EC2BUnofficialPatch.dll`，版本 `1.0.22`，对应游戏 `1.94`，大小 558592 字节，SHA-256 为 `71474b74ea9befc945872160323a4b808212168a20aaf3c2469369d111f121ce`。根目录、`release-manifests/` 与 `dist/release/` 的 `update.json` 完全一致，清单大小及哈希与发布 DLL 相符。游戏内运行测试和 GitHub 发布尚未进行。

2026-09-21：基于提交 `7c9d16b` 的独立克隆修改，原项目未改动。视频 1164、5002 歌词音频与内置《更好的音频效果》共用原版音乐通道优先级；视频从启动到真正退出一直持有暂停，循环到下一轮不会释放。跳过、`1164,0`、替换、报错和离开场景均通过统一结束路径释放。BA 的独立音源不被视频暂停；若 BA 或 5002 仍在播放，原版音乐继续保持暂停。

视频配置取消 `block`，由 `roleOnTop` 决定动态背景或盖住界面；动态背景优先于 `skippable`。盖住界面且允许跳过时，长按左键约一秒，复用原版歌词跳过素材。旧 JSON 中的 `block` 会被忽略。

配置区合并为“屏幕特效与效果”，3003 迁入“优化”；《更好的音频效果》及视频模块在“机制”中分别有启用开关（默认开启）和信息日志开关（默认关闭），旧开关值迁移。作者模板照常创建，但内容扫描不把插件安装目录的模板作为 Mod 数据读取。Release 构建通过（net472，0 错误，1 个原有未使用字段警告）；运行时尚未在游戏内实测。

复核：旧 `[屏幕特效] 1164屏幕视频扩展` 仅作为配置迁移输入，新 cfg 不再生成该键，视频开关统一写在 `[机制]`。5002 继续先解析原版 AudioCfg，再解析 BA 音频 ID；BA 关闭时仍支持原版。5002 播放自身音频期间持有原版音乐暂停，结束时先归还原有 BA 会话，再释放原版通道；内置 BA 的解析与暂停接口改为直接调用，移除了同程序集内的反射桥接。视频替换改走完整释放路径，动态背景无法嵌入对话时跳过播放。

本版统一为 UP 单 DLL、单 BepInEx 入口，音频演出内置，不再发布 BA 或分离版。音频功能由 UP 持久运行时初始化、保活和清理，F9 热重载直接使用内置音频注册表。1.0.24 的音频弱引用/有界日志缓存、版本比较和测试输出隔离，以及 1.0.25 的原 schema=1 更新流程、layout 匹配、嵌入助手和退出后单文件替换，均以合并版实现保留。

保留 Mod 的旧 JSON、资源路径、1163 指令及 UP 小游戏接口。根工程为 EC2BUnofficialPatch.csproj。在线更新恢复原 update.json 入口及 EC2BUnofficialPatch.dll 发布附件名。旧 BA 用户升级前须移除独立 BA DLL。1.0.23～1.0.25 测试版改为本版需手动覆盖一次。

[安装与完整功能说明](../../README.md) · [结构与兼容](../../Docs/MERGE.md) · [在线更新](../../Docs/AUTO_UPDATE.md) · [验证范围](../../Docs/VALIDATION.md)

原 CHANGELOG/Validation 文件仍保留历史版本标注；1.0.24/1.0.25 的内容和验收边界已在 1.0.21 当前记录中合并说明。
