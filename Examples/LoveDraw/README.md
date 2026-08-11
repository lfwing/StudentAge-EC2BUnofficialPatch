# LoveDraw 外置资源示例

把本目录结构复制到 Workshop Mod 根目录，或者放到 `EC2BUnofficialPatch/LoveDraw` 下。

原版 `Cfgs/zh-cn/LoveDrawCfg.json` 示例：

```json
{
  "10107": {
    "id": 10107,
    "name": "外置情侣画示例",
    "img": "paint/example_10107.png",
    "video": "paint/example_10107.mp4",
    "cond": [],
    "talkId": []
  }
}
```

也可以写成不带扩展名的形式：

```json
"img": "paint/example_10107",
"video": "paint/example_10107"
```

图片与视频路径必须相对于 `LoveDraw` 目录，不能使用 `..`。

## 1.0.14 双版本 CFG

如果同一个 Workshop Mod 需要兼容未安装 EC2BUnofficialPatch 的玩家，可把兼容版继续放在：

```text
Cfgs/zh-cn/LoveDrawCfg.json
```

把使用外置 LoveDraw 资源的增强版放在：

```text
EC2BUnofficialPatch/LoveDraw/LoveDrawCfg.json
```

“情侣画修复”开启时，插件只在**当前这个 Mod**内将本次读取切换到增强版；不会覆盖兼容版，也不会读取其他 Mod 的同名 CFG。
兼容版必须继续存在，因为原版会先从 `Cfgs/zh-cn/*Cfg.json` 枚举配置文件。
