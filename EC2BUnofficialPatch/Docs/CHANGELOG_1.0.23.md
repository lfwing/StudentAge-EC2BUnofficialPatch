# UP 1.0.23

面向《学生时代》1.94。

## CG 图鉴排序

- Mod Group 3 中，`idx > 0` 的 CG 优先按 `idx` 升序排列；相同 `idx` 按 CG ID 升序。
- 未设置 `idx` 的 CG 随后按 ID 升序排列；显示图号从 `001` 开始连续编号，不直接使用原始 `idx` 作为图号。

## 羽毛球角色外置贴图

- 新增 `custombadminton/custombadmintoncfg.json` 注册表，字段沿用 `BadmintonModelCfg.id/url`，并增加 `prefab` 指定原版模板。
- 支持普通 NPC；`id=0` 还可按 `url[0]/url[1]` 覆盖男女玩家模型。
- 每个 url 从同名子目录读取 `<url>_ske.json`、`<url>_tex.json` 和 tex 的 `imagePath` 指定 PNG。
- 复用模板 prefab 的 Animator、`AnimationEvt`、碰撞节点和 UI 层级；仅重建当前实例的 DragonBones Armature。
- 不要求外置 `.mat`。运行时克隆模板材质并替换纹理，避免修改共享材质。
- 对象池把同一 prefab 交回原版角色时恢复模板 DragonBones 数据；加载失败也回退模板角色。
- F9 注册为独立热重载提供者，重读配置和 ske、tex、PNG 的文件版本；现有场上实例不强制刷新，下次创建时应用。
- 修复初版在原 `Armature` 宿主内当帧释放并重建，使新骨骼复用随后被 Unity 延迟销毁的插槽对象，造成外置角色不显示、`UnitySlot._UpdateTransform` 空引用及共享时钟中的玩家动画停滞。现在先在全新宿主构建并校验插槽和七个玩法动画，再替换旧宿主。

## 构建

- 保持 `AssemblyVersion=1.0.9.0`，产品、文件与信息版本更新为 `1.0.23`。
- 本地候选 DLL：580608 字节，SHA-256 `285a9fafec781f1b0846fc8f4272f9238cc0859f0419a6975be674b0ab73257e`。
- net472 编译通过：0 错误，1 个既有未使用字段警告。
- 修复后游戏内动画、对象池复用、多 Mod 组合及 CG 图鉴排序尚待复测。
