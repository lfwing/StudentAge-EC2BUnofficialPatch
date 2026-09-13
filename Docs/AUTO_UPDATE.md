# UP 1.0.21 自动更新

仅维护完整 UP：音频演出已经内置，取消分离版和独立 BA 更新。沿用现有合并版的后台检查、HTTPS 下载、大小/SHA256 校验、内嵌助手、退出替换和备份恢复流程。

## 唯一更新通道

- 清单文件：`update.json`。
- 必需标记：`schema: 1`、`layout: merged`，保留现有协议与地址约定。
- 下载资产：`EC2BUnofficialPatch.dll`；游戏中的安装文件仍为 `EC2BUnofficialPatch.dll`。
- 顺序：用户配置的 HTTPS 镜像 → GitHub Release → GitHub Raw。
- 拒绝 split、未标记、旧 schema2、不匹配文件名、无效大小/摘要或非 HTTPS 地址。
- 原六参数助手等待游戏退出后替换实际加载位置中的 UP DLL，支持 Workshop 路径，并保留备份和损坏拒绝。它不会删除或升级其他插件。

## 版本修订与首次迁移

当前插件版本为 1.0.21。已安装 1.0.23～1.0.25 接手测试版的玩家，需要退出游戏并手动覆盖一次；保留禁止自动降级的行为。此后发布更高版本时按通常版本比较更新。

旧独立 BA 用户需要先移除 LFBetterAudio.dll，再安装完整 UP。原 BetterAudio.json、资源目录及 1163 指令保持兼容。硬依赖独立 BA GUID/程序集的第三方 DLL 需重新适配 UP。

## 发布顺序

运行 `build.py --game ... --bepinex ...` 后，只生成 `dist/merged/EC2BUnofficialPatch.dll`。`tools/prepare_release.py` 校验构建清单和最终 DLL，准备 `dist/release/` 中的一个 DLL 及一份清单，并写入 `release-manifests/update.json`。

先上传同版本 DLL，再启用 Release/仓库根目录的 `update.json`。构建脚本不覆盖线上清单、不自动创建 Release。根目录 `update.json` 在发布对应 DLL 后同步更新，供旧版与新版共用。支持 schema 1 更新协议的旧 UP 可读取新清单，额外的 layout 字段不影响旧解析器。没有更新模块的早期版本仍需手动安装。旧 BA 必须在升级前移出插件加载目录，更新助手不会自动移除它。
