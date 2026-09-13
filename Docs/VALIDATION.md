# UP 1.0.21 验证范围

- 交付构建与根 EC2BUnofficialPatch.csproj 的 Release 构建均成功。修正了 MSBuild 从测试输出目录误选 Newtonsoft.Json 的引用顺序，明确优先本机游戏引用；现有一个音频控制器未读私有字段警告保留。
- 更新器 26 项定向检查通过：仅接受 merged 清单，拒绝分离/无标记/非法清单，保留镜像→Release→Raw 顺序、版本比较、HTTPS/摘要/大小校验、plugins/Workshop 位置替换、备份和损坏拒绝。助手测试使用临时目录与 pid=0，不代表正式在线更新验收。
- 隔离 CrossOver 游戏启动完成 22 项音频接管检查：UP 版本 1.0.21、DLL 中仅一个 BepInEx 入口、无独立 BA 注册、持久音频控制器、重复初始化、音频补丁归属、1163 播放真实 WAV、5002 暂停/恢复、资源注册表热重载保留播放、旧暂停/恢复/停止指令和退出清理均通过。
- 本轮不重复小游戏、Live2D 或完整周目联调；这些功能的历史 1.0.24 检查不能替代本版完整验收。未进行原生 Windows UI 或正式发布验收。
- 首次从 1.0.23～1.0.25 测试版切回 1.0.21 需要手动覆盖，不放开自动降级。

## 交付 DLL

EC2BUnofficialPatch.dll：537600 bytes，SHA256 `75e7ea0bb4d706bb7fd607e25369db7913c0c148d79f1308760c8d2905ec589b`。插件版本/产品版本 1.0.21，文件版本 1.0.21.0；UP 程序集绑定身份保持 1.0.9.0。

构建仅准备 release-manifests/update-merged.json 和发布资产，线上旧 update.json 不变，没有自动发布 Release。
