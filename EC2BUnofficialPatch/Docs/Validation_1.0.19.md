# 1.0.19 验证记录

## 构建与嵌入

- 删除更新助手的 `bin/obj` 后，从干净状态构建 Release/net472：通过，0 警告、0 错误。
- 主项目自动还原并构建 `UpdaterHelper`，无需预先存在的 EXE。
- 主 DLL 清单资源包含且仅包含 `EC2BUnofficialPatch.Embedded.Updater.exe`。
- 从主 DLL 提取的助手与构建输出大小、SHA-256 完全一致。

## 路径验证

- 更新目标优先取 `Chainloader.PluginInfos[PluginMetadata.Guid].Location`，无登记时回退 `Assembly.Location`。
- 两个来源均为空、来源文件不存在或目标文件名不是 `EC2BUnofficialPatch.dll` 时拒绝更新。
- 模拟 Workshop 来源：`.../workshop/content/1991040/3781961569/BepInEx/plugins/EC2BUnofficialPatch/EC2BUnofficialPatch.dll`。
- 助手提取到上述 DLL 旁的 `.EC2BUnofficialPatch.Update/<hash>/EC2BUnofficialPatch.Updater.exe`。
- 模拟替换成功：目标内容更新、旧内容写入同目录 `.backup`、同目录 `.pending` 被消费，游戏主插件目录未被触碰。

## 发布结构

- GitHub Release 只需上传 `EC2BUnofficialPatch.dll`。
- 仓库 `main` 根目录保留与最终 DLL 一致的 `update.json`。
- 玩家无需额外安装更新助手；仅在确实发现更高版本时提取内嵌助手。
