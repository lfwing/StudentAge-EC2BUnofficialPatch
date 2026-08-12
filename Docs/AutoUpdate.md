# EC2BUnofficialPatch 自动更新发布说明

## 玩家端文件

完整安装包只需要包含：

```text
BepInEx/plugins/EC2BUnofficialPatch.dll
```

更新助手已经作为二进制资源嵌入主插件。主插件负责异步检查、下载和 SHA-256 校验；发现新版后，自动把助手提取到当前 DLL 旁的隐藏更新目录。助手等待游戏退出后替换当前正在运行的 DLL，玩家不需要额外安装 EXE。

更新目标优先取 BepInEx `PluginInfo.Location` 记录的插件来源路径，标准加载时与当前程序集的 `Assembly.Location` 一致；非标准加载器未登记时才回退到 `Assembly.Location`。因此插件既可以位于游戏目录的 `BepInEx/plugins`，也可以由桥接补丁从 Workshop 内容目录加载；待安装文件、备份和助手都会紧邻当前 DLL，不使用固定游戏插件路径。

## 发布文件

当前发布方式下，每个 GitHub Release 只需上传：

```text
EC2BUnofficialPatch.dll
```

仓库 `main` 根目录必须提交对应的 `update.json`。标签必须与清单版本一致，例如 `1.0.19`。不要把 Release 标记为草稿或预发布，否则 `releases/latest` 不会将其视为稳定版。

## 自动生成

在仓库根目录运行：

```powershell
.\publish.ps1 -GameDir "G:\SteamLibrary\steamapps\common\StudentAge"
```

如有国内 DLL 镜像，可同时生成镜像优先的清单：

```powershell
.\publish.ps1 `
  -GameDir "G:\SteamLibrary\steamapps\common\StudentAge" `
  -MirrorDownloadUrls "https://example.cn/ec2b/1.0.19/EC2BUnofficialPatch.dll"
```

脚本会：

1. 编译更新助手，并自动嵌入主插件；
2. 生成完整安装 ZIP；
3. 计算主 DLL 的大小和 SHA-256；
4. 更新仓库根目录的 `update.json`；
5. 在 `dist` 中生成 DLL、清单和可选安装 ZIP。

应先创建并上传 GitHub Release，再提交更新后的 `update.json` 到主分支，避免旧客户端读取到尚未存在的下载地址。

## 多源清单

玩家可以在 CFG 的 `备用更新清单地址` 填写一个或多个完整 HTTPS 地址，以英文分号分隔。插件的顺序为：

1. CFG 中的镜像清单；
2. GitHub 最新 Release 附带的 `update.json`；
3. GitHub Raw 主分支中的 `update.json`。

镜像中的 `update.json` 应与 GitHub Release 使用相同内容；DLL 也必须字节一致，否则 SHA-256 校验会拒绝安装。

## 安全边界

- 仅接受 HTTPS 清单和下载地址；
- 仅接受文件名 `EC2BUnofficialPatch.dll`；
- 清单最大 256 KiB，DLL 最大 32 MiB；
- 下载后和退出后替换前各校验一次 SHA-256；
- 更新助手由主 DLL 内嵌资源提取并按自身 SHA-256 校验；
- 目标、pending 和 backup 必须位于同一目录；
- 替换后的 DLL 再次校验，失败时尝试从 backup 回滚；
- 不更新任何玩家可编辑模板和 CFG。

SHA-256 用于保证镜像文件和官方清单一致。若未来允许不受作者控制的第三方镜像提供清单，应再增加内嵌公钥的数字签名验证。
