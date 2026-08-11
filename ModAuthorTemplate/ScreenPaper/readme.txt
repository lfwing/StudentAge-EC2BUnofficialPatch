5001 屏幕纸条扩展

纸条内容继续在原版 PaperCfg.json 注册，本文件只指定已注册纸条 ID 使用的图片。
把 PNG/JPG/JPEG 图片放在本目录，并在 Custompaper.json 的 papers 数组中填写 id 与 image。
image 是相对于本目录的路径。未声明、留空、缺图或坏图时使用原版图片。
未在 PaperCfg 注册的 ID、越界路径或重复 ID 会报错；冲突 ID 会全部回退原版。
