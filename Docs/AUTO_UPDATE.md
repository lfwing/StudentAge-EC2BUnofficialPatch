# UP 1.0.23.1 自动更新与发布

仅维护完整 UP：音频演出已经内置，取消分离版和独立 BA 更新。沿用现有合并版的后台检查、HTTPS 下载、大小/SHA256 校验、内嵌助手、退出替换和备份恢复流程。

## 唯一更新通道

- 清单文件：`update.json`。
- 必需标记：`schema: 1`、`layout: merged`，保留现有协议与地址约定。
- 下载资产：`EC2BUnofficialPatch.dll`；游戏中的安装文件仍为 `EC2BUnofficialPatch.dll`。
- 顺序：用户配置的 HTTPS 镜像 → GitHub Release → GitHub Raw（兼容回退，根目录不再发布清单）。
- 拒绝 split、未标记、旧 schema2、不匹配文件名、无效大小/摘要或非 HTTPS 地址。
- 原六参数助手等待游戏退出后替换实际加载位置中的 UP DLL，支持 Workshop 路径，并保留备份和损坏拒绝。它不会删除或升级其他插件。

## 版本修订与首次迁移

当前源码版本为 1.0.23.1，面向游戏 1.94。1.0.23可按通常版本比较升级到1.0.23.1；已安装1.0.24～1.0.25接手测试版的玩家，仍需退出游戏并手动覆盖一次，保留禁止自动降级的行为。

旧独立 BA 用户需要先移除 LFBetterAudio.dll，再安装完整 UP。原 BetterAudio.json、资源目录及 1163 指令保持兼容。硬依赖独立 BA GUID/程序集的第三方 DLL 需重新适配 UP。

## 发布顺序

运行 `build.py --game ... --bepinex ...` 后，生成 `dist/merged/EC2BUnofficialPatch.dll`。`tools/prepare_release.py` 校验构建清单和最终 DLL，准备 `dist/release/` 中的一个 DLL 及一份清单，并同步写入 `release-manifests/update.json` 和仓库根目录 `update.json`；三份清单的版本、大小和SHA-256必须与DLL一致。

构建先生成并验证本地候选，之后可提交源码、文档和候选清单。Git推送不会创建Release或上传附件；维护者须另行创建1.0.23.1 Release并上传同次构建的EC2BUnofficialPatch.dll与update.json。附件发布前，仓库里的清单仍只是候选，不能据此宣称完整更新包已可下载。客户端 Raw 查询保留为兼容回退。支持 schema 1 更新协议的旧 UP 可读取新清单，额外的 layout 字段不影响旧解析器。没有更新模块的早期版本仍需手动安装。旧 BA 必须在升级前移出插件加载目录，更新助手不会自动移除它。

## 本版维护者核对

- 三处清单：仓库根目录update.json、release-manifests/update.json、dist/release/update.json。
- Release附件取dist/release/EC2BUnofficialPatch.dll和同目录update.json；以本次实际构建的size/SHA-256为准。
- 版本、FileVersion和ProductVersion为1.0.23.1，AssemblyVersion仍为1.0.9.0。
- build.py直接读取csproj的FileVersion，支持四段发布号；不要将其拼成1.0.23.1.0。
- Release由维护者手动上传；工坊内容另行发布，源码提交不等于工坊更新。
