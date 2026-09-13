# 合并结构

源代码按 EC2BUnofficialPatch 与 LFBetterAudio 两目录维护，原命名空间、GUID和程序集身份不变。根 StudentAge.Merged.csproj 构建单DLL双入口；build.py支持merged/split两种布局。

BA只扫描自身HarmonyPatch类型；UP音频追踪按类型全名识别BA。双方保留各自配置文件、资源路径及生命周期。小游戏使用UP公开导入接口，由外部实现负责界面，UP统一阶段与奖励。

在线更新沿用schema1、上游内嵌单文件助手和实际来源路径。layout标记与独立合并清单防止两种布局交叉覆盖，split仅更新UP。当前协议见AUTO_UPDATE.md，验证范围见VALIDATION.md。
