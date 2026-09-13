# 当前状态：UP 1.0.21（合并 1.0.24/1.0.25 内容）

本版统一为 UP 单 DLL、单 BepInEx 入口，音频演出内置，不再发布 BA 或分离版。音频功能由 UP 持久运行时初始化、保活和清理，F9 热重载直接使用内置音频注册表。1.0.24 的音频弱引用/有界日志缓存、版本比较和测试输出隔离，以及 1.0.25 的原 schema=1 更新流程、layout 匹配、嵌入助手和退出后单文件替换，均以合并版实现保留。

保留 Mod 的旧 JSON、资源路径、1163 指令及 UP 小游戏接口。根工程为 EC2BUnofficialPatch.csproj。在线更新恢复原 update.json 入口及 EC2BUnofficialPatch.dll 发布附件名。旧 BA 用户升级前须移除独立 BA DLL。1.0.23～1.0.25 测试版改为本版需手动覆盖一次。

[安装与完整功能说明](../../README.md) · [结构与兼容](../../Docs/MERGE.md) · [在线更新](../../Docs/AUTO_UPDATE.md) · [验证范围](../../Docs/VALIDATION.md)

原 CHANGELOG/Validation 文件仍保留历史版本标注；1.0.24/1.0.25 的内容和验收边界已在 1.0.21 当前记录中合并说明。
