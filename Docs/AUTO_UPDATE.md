# 自动更新：沿用 UP 原流程

当前：1.0.25-handoff.4。复用上游 schema 1 清单、后台检查、HTTPS下载、大小与SHA256校验、内嵌单文件助手、游戏退出后替换与备份回滚。移除本接手版增加的 schema 2、双文件事务及强制游戏程序集哈希门槛。

## 安装类型匹配

原清单只增加一个字段：`layout`。合并版填 `merged`，分离版填 `split`；字段缺失或不匹配均跳过，不能让旧 UP 覆盖含 BA 的合并 DLL。schema 仍为1，原字段和旧客户端解析方式不变。

- 合并版查 `update-merged.json`，下载 `merged-EC2BUnofficialPatch.dll`，安装时仍叫 `EC2BUnofficialPatch.dll`。
- 双 DLL 版查原 `update.json`，下载 `split-EC2BUnofficialPatch.dll`；只更新 UP，保留现有 LFBetterAudio.dll。需要升级独立 BA 时使用完整安装包，不做双文件自动升级或布局迁移。
- 仍按“用户配置镜像 → 原仓库 Latest Release → 原仓库 main Raw”顺序检查。镜像须提供当前类型的清单。
- 目标仍取 BepInEx 登记的实际 DLL 来源；原有 Workshop 桥接加载位置更新恢复，pending/backup/助手紧邻该 DLL。不修改 Mod JSON 或存档。

当前原作者公开版本没有 layout 标记，因此会被新版安全跳过。不是要求另建仓库：原作者发布相应清单和 DLL 后即可使用。仓库地址保持原 UP 仓库。

## 维护者发布

```
python3 build.py --game "游戏目录" --bepinex "BepInEx/core目录" --layout all
python3 tools/prepare_release.py
```

默认目标仓库 `lfwing/StudentAge-EC2BUnofficialPatch`；也可用 `--repository owner/repo` 准备其它来源，但对应客户端须配置镜像。脚本只生成本地产物，不上传。`dist/release` 含两份 UP 产物、分离 BA 和两份 schema1 清单。DLL 大小/摘要必须和清单一致。

先发布同版本匹配的 DLL，再启用对应清单；根 `update.json` 必须始终为 split，防止旧客户端意外安装合并 DLL、与其已有 BA 重复加载。合并版使用独立的 `update-merged.json`。不要提前把指向未上传 DLL 的候选清单覆盖公开更新源。

如存在依赖此前1.0.23/1.0.24接手版schema2的测试安装，手动安装本版一次；不提供跨协议自动迁移。公共上游仍是schema1。旧助手的六参数契约、提取方式和替换实现本轮原样恢复。
