# 4016 Comic 模板

把 `comic` 文件夹复制到你的 Workshop Mod 内任意位置，例如：

```text
EC2BUnofficialPatch/comic/cg_01/
```

图片严格使用 `{图号}-{分镜号}.png/.jpg/.jpeg`。

CGCfg 推荐逻辑路径：

```text
Mods/<packageId>/EC2BUnofficialPatch/comic/cg_01
```

`packageId` 是 `ModMetadata.packageId`，不是 Workshop 数字 ID；`Mods/<packageId>/` 是游戏逻辑前缀，不是物理目录。新 JSON 推荐使用 `/`，旧式 `\\` 同样兼容。
