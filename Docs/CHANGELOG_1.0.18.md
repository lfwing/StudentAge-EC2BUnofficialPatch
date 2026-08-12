# EC2BUnofficialPatch 1.0.18

## 外置媒体路径兼容修复

- 移除情侣画外置视频对本地路径的 `file://` URI 强制转换，直接向 Unity `VideoPlayer` 传递 Windows 绝对路径。
- 首次直接播放失败后，自动在 `BepInEx/cache/EC2BUnofficialPatch/NativeMedia` 创建纯 ASCII 文件名的媒体副本并重试一次。
- 缓存名根据源路径生成；源文件没有变化时复用已有副本，源文件发生变化时原位刷新，避免反复留下旧缓存。
- 缓存只用于 Unity 原生媒体后端的兼容回退，不修改 Workshop 原文件，也不介入图片、CFG、JSON 或外部 DLL 加载。
- 最终失败日志会同时保留直接路径尝试和缓存路径尝试的错误信息，便于区分路径问题与视频编码问题。
