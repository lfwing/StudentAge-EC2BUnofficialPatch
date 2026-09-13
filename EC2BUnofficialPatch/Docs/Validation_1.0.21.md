# 1.0.21 验证记录

## 外置 Live2D

- 复核游戏 1.93 的 `MapRoleView`、`Cell_NewTalkRoleItemUI` 与 `TalkRoleItem`：地图角色立绘容器为 `l2d_role`，静态图容器为 `icon_role`，关闭界面时原版只回收自己加载的 `L2DModel`。
- 复核当前游戏 `Live2D.Cubism.dll`：`CubismModel3Json.LoadAtPath` 支持自定义文件加载委托，`ToModel` 负责 moc、Drawable、Renderer、遮罩与可选物理初始化；模块直接使用这些游戏内 API。
- 运行时加载前校验 `model3.json` Version、Moc/Textures 引用、moc 一致性、PNG 文件和可选 physics JSON；实例化后再次校验模型 moc 版本不高于 `CubismMoc.LatestVersion`，并确认所有 Drawable 已绑定纹理。
- `model` 与模型包内引用都要求位于各自允许的目录内；绝对路径、`..`、非 PNG 纹理、重复角色服装组合均不能进入显示路径。
- 每次 `MapRoleView.Refresh` 先隐藏上一次外置实例，再按 `personId + requestedCloth + schoolStage` 查找，精确小学/中学项优先于 `all`；无匹配或失败时保留 `MapRoleStaticClothModule` 已选出的静态服装。
- `MapRoleView.OnExit` 隐藏最近模型以供相同模型再次打开时复用；界面实例销毁或切换到另一外置模型时，清理旧模型 GameObject、运行时 CubismMoc 与 Texture2D。
- 根据实测模型 152400 修正布局：旧 `scale/x/y` 是 Cubism 本地绝对量，无法跨不同画布直观复用；新自动布局排除静态 PNG 透明留白，并按当前可见 Drawable 范围换算，不使用该模型的纹理尺寸作为全局常量。两组归一化锚点可对齐姿势不同素材的同一人体位置。
- 实测模型声明了 Angle、EyeBall、EyeOpen、BodyAngle、Breath 与头发物理参数；基础驱动按参数 ID 存在性绑定，并以执行顺序 750 写入，早于 Cubism Physics 的 800。
- 对全部 Masked Drawable 按遮罩源组合计数；确认本游戏运行时 `ToModel` 创建的控制器没有序列化 MaskTexture。属性 Setter 会先在旧 Junction 上分配 Tile，随后 `ForceRevive` 又替换 Junction，导致新对象无 Tile。最终实现绕开 Setter：停用控制器、写入游戏 GlobalMaskTexture 字段、重建 Junction，最后启用并首次分配 Tile。
- PNG 通过 Unity 异步纹理通道读取和解码，完成前继续显示静态立绘；同一 `ExternalLive2DInstance` 重复打开相同模型时保留并恢复原实例，切换到不同外置模型时才销毁旧实例。model3、moc3 与 physics3 在同一次构建中只读取一次。
- 成功日志以模型路径为键在单次游戏进程内只输出一次；错误日志仍保留，避免重复打开时刷出大量正常日志。
- 当前工作树未包含 Mod 模型包，因此没有伪造实机画面结论；目标模型的尺寸、位置、遮罩和物理表现需使用模板中的 `fitScale`、两组锚点、`xOffset/yOffset/flip` 在游戏内完成最终验收。旧 `scale/x/y` 仅用于兼容已有配置。

## 地图静态 Mod 角色随机服装

- 复核游戏 1.93 的 `MapRoleView.Refresh`：普通地图原版传入服装 0，学校地图传入 1。
- 补丁仍以 `MapRoleView.Refresh` 的线程内调用范围为边界；普通地图读取 `Role.ClothId`，学校地图保持 1，静态差分不可用时回退 0。
- 原版角色、原版 Live2D、`DetailSocialView`、Talk 与剧情 3006 不在该替换路径内。

## 游戏内调试控制台

- `[Debug] AutoOpenGameConsole` 默认 `false`；关闭时不安装 `DebugView` 补丁。
- 开启后仍在游戏 1.93 的 `DebugView.Start` 后调用原版显示逻辑；输入框、反引号和 Esc 状态机没有复制或改写。

## 构建与发布产物

- Debug/net472：通过，0 警告、0 错误。
- Release/net472：通过，0 警告、0 错误。
- 文件版本：`1.0.21`；程序集版本继续保持 `1.0.9.0`。
- `EC2BUnofficialPatch.dll`：381440 bytes。
- SHA-256：`53F76E8CEB14816C97AECC045CFD96FCC0FA39F1B82862D12D21EEF7AA81ABD3`。
- 根目录与 `dist/update.json` 的版本、大小、哈希和 1.0.21 下载地址与发布 DLL 一致。
- 发布 ZIP 共 10 个条目，包含主 DLL、README、既有模板和新增 `ExternalLive2D` 模板。
