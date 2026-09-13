# UP 1.0.21 统一维护

只构建和发布 `EC2BUnofficialPatch.dll`，仅注册 `sa.EC2B.UnofficialPatch` 一个 BepInEx 入口。根工程为 `EC2BUnofficialPatch.csproj`，`build.py` 不再提供安装布局选项。

音频源码迁入 `EC2BUnofficialPatch/Features/Audio/`，由 UP 持久运行时负责初始化与退出清理。音频控制器继续保活，1163 补丁归属为 `sa.EC2B.UnofficialPatch.Audio`，F9 音频热重载不再依赖独立 BA 插件注册。

旧 `LFBetterAudio` 命名空间与部分类型名仅为兼容保留，不再代表独立插件。旧 Mod 的 BetterAudio 资源目录、BetterAudio.json、Timeline 与 1163 指令均不改名。程序集身份仍为 EC2BUnofficialPatch 1.0.9.0，插件版本、文件版本和产品版本为 1.0.21；程序集身份版本用于保持既有 UP 小游戏适配器绑定，不是发布版本。

停止提供独立 BA DLL 和分离版构建工程。依赖独立 BA GUID 或程序集的第三方插件需要改为依赖 UP 并重新构建。普通 Mod 的数据与资源无需迁移。

1.0.24/1.0.25 接手内容已回并到当前 1.0.21：音频监控使用 AudioClip 弱引用和有界日志集合，版本比较缺省值按 0 处理；更新器恢复 schema 1 单文件助手，并以 `layout=merged` 拒绝错误布局。

在线更新保留原合并版通道，见 [自动更新](AUTO_UPDATE.md)。实际验证范围见 [验证记录](VALIDATION.md)。历史 1.0.23～1.0.25 文件保留为开发过程证据，其有效内容已在 1.0.21 记录中回并。
