# 1.0.17 验证记录

- 主插件与独立更新助手均以 .NET Framework 4.7.2 Release 编译。
- 更新清单强制校验 schema、稳定通道、语义版本、固定 DLL 文件名、大小、SHA-256 与 HTTPS 下载地址。
- 更新助手成功路径验证：pending 替换为目标 DLL，旧目标保留为 backup，安装后 SHA-256 与清单一致。
- 错误 SHA-256 验证：助手返回失败，不修改目标 DLL、不创建 backup，并保留 pending 供诊断。
- 完整源码包解压后二次编译。
