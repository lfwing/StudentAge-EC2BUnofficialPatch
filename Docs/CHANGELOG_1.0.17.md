# EC2BUnofficialPatch 1.0.17

## 自动更新

- 启动完成后异步检查稳定版本，不阻塞游戏主线程。
- 支持自定义 HTTPS 镜像清单、GitHub Latest Release 清单和 GitHub Raw 清单依次回退。
- 支持检查间隔，网络失败只影响本次更新，不影响插件功能。
- 发现新版后按配置自动下载；严格校验文件名、大小和 SHA-256。
- 使用独立更新助手等待游戏退出后替换 DLL，保留上一版本 `.backup`，替换失败时尝试回滚。
- 自动更新不会修改 BepInEx CFG、Workshop CFG、纸条、歌词或其他玩家资源。

## 发布工具

- `build.ps1` 同时编译主插件和更新助手。
- 新增 `publish.ps1`，自动生成 `update.json`、完整安装包和 GitHub Release 上传文件。
