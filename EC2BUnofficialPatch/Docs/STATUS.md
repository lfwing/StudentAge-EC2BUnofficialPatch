# 当前状态：UP 1.0.21

本版统一为 UP 单 DLL、单 BepInEx 入口，音频演出内置，不再发布 BA 或分离版。音频功能由 UP 持久运行时初始化、保活和清理，F9 热重载直接使用内置音频注册表。

保留 Mod 的旧 JSON、资源路径、1163 指令及 UP 小游戏接口。根工程为 EC2BUnofficialPatch.csproj。在线更新恢复原 update.json 入口及 EC2BUnofficialPatch.dll 发布附件名。旧 BA 用户升级前须移除独立 BA DLL。1.0.23～1.0.25 测试版改为本版需手动覆盖一次。

[安装与完整功能说明](../../README.md) · [结构与兼容](../../Docs/MERGE.md) · [在线更新](../../Docs/AUTO_UPDATE.md) · [验证范围](../../Docs/VALIDATION.md)

原 CHANGELOG/Validation 文件是历史开发记录，保留原始版本标注，不视为当前版本或本轮验收。
