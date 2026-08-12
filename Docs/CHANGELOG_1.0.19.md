# EC2BUnofficialPatch 1.0.19

## 单 DLL 自动更新

- 更新助手已嵌入 `EC2BUnofficialPatch.dll`，玩家不再需要手动安装或保留单独的更新助手 EXE。
- 发现新版并完成下载校验后，插件会自动提取助手；提取文件按内嵌内容 SHA-256 校验并复用。
- 更新目标优先取 BepInEx `PluginInfo.Location` 登记的真实来源路径，无法取得时才回退到当前程序集的 `Assembly.Location`；不再从固定的 `BepInEx/plugins` 路径查找任何组件。
- `pending` 与 `backup` 始终创建在当前 DLL 同目录；助手也提取到当前 DLL 旁的 `.EC2BUnofficialPatch.Update` 目录。
- 支持由桥接补丁从 Workshop 目录加载的插件实例，例如：
  `.../workshop/content/1991040/<modId>/BepInEx/plugins/EC2BUnofficialPatch.dll`。
- GitHub Release 可继续只上传一个 `EC2BUnofficialPatch.dll`；`update.json` 提交在仓库 `main` 根目录即可。

## 构建稳定性

- 主项目会在构建前自动构建更新助手并将其嵌入，不依赖预先存在的 EXE。
- 补充 `Live2D.Cubism.dll` 显式引用，修复全新或清理后的工程可能无法编译的问题。
