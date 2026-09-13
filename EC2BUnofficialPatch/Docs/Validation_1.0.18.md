# 1.0.18 验证记录

## 构建

- `EC2BUnofficialPatch.csproj` Release/net472：通过，0 警告、0 错误。
- `EC2BUnofficialPatch.Updater.csproj` Release/net472：通过，0 警告、0 错误。
- DLL 文件版本与产品版本：`1.0.18.0` / `1.0.18`。

## 路径兼容检查

- 全项目仅情侣画外置视频把物理文件路径交给 Unity `VideoPlayer`；旧版 `new Uri(...).AbsoluteUri` 已移除。
- 外置图片、纸条图片、漫画图片使用 `File.ReadAllBytes`；CFG/JSON 使用 `File.ReadAllText`；外部小游戏 DLL 使用 `Assembly.LoadFrom`，均不进行 URI 转义。
- 视频首先使用 Windows 本地绝对路径；首次失败后才创建纯 ASCII 缓存并重试，避免正常资源产生多余副本。
- 缓存以源路径哈希命名，并比较文件大小和最后修改时间；相同源文件复用，源文件变化时原位刷新。
- 已用实际中文文件路径验证缓存文件名为 ASCII，且保持原视频扩展名。

## 运行时预期

- 直接路径成功：日志显示原始物理路径，不创建缓存。
- 直接路径失败、缓存成功：先记录一次兼容重试警告，随后正常开始播放。
- 两次均失败：错误同时列出直接尝试和缓存尝试结果，此时优先检查视频编码兼容性。
