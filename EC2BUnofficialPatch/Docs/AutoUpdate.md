# UP 1.0.21 自动更新（合并 1.0.25 流程）

当前：1.0.21。复用上游 schema 1 清单、后台检查、HTTPS 下载、大小与 SHA256 校验、内嵌单文件助手、游戏退出后替换与备份回滚。合并版额外保留 `layout=merged` 匹配，拒绝分离版、schema 2 和不匹配布局。

## 安装类型匹配

原清单只增加一个字段：`layout`。当前合并版填 `merged`；字段缺失或不匹配均跳过，不能让旧 UP 覆盖含 BA 的合并 DLL。schema 仍为 1，原字段和旧客户端解析方式不变。

- 合并版查 `update.json`，下载 `EC2BUnofficialPatch.dll`，安装时仍叫 `EC2BUnofficialPatch.dll`。
- 当前不发布分离版或独立 BA 更新；旧 BA 用户须先移除独立 DLL，再安装合并版 UP。
- 仍按“用户配置镜像 → 原仓库 Latest Release → 原仓库 main Raw”顺序检查；根目录不再发布清单，Raw 仅为兼容回退。镜像须提供当前类型的清单。
- 目标仍取 BepInEx 登记的实际 DLL 来源；原有 Workshop 桥接加载位置更新恢复，pending/backup/助手紧邻该 DLL。不修改 Mod JSON 或存档。

当前原作者公开版本没有 layout 标记，因此会被新版安全跳过。不是要求另建仓库：原作者发布相应清单和 DLL 后即可使用。仓库地址保持原 UP 仓库。

## 维护者发布

```
python3 build.py --game "游戏目录" --bepinex "BepInEx/core目录"
python3 tools/prepare_release.py
```

默认目标仓库 `lfwing/StudentAge-EC2BUnofficialPatch`；也可用 `--repository owner/repo` 准备其它来源，但对应客户端须配置镜像。脚本只生成本地产物，不上传。合并版 DLL 的大小/摘要必须和清单一致。

先发布同版本匹配的 DLL 与 `update.json` Release 附件，再启用公开更新；不要提前把指向未上传 DLL 的候选清单覆盖公开更新源。仓库根目录不再维护 `update.json`。

如存在此前 1.0.23/1.0.24 接手版 schema2 的测试安装，手动安装本版一次；不提供跨协议自动迁移。公共上游仍是 schema1。旧助手的六参数契约、提取方式和替换实现已合并恢复。
