# UP 1.0.23.1 单DLL与兼容说明

当前面向游戏1.94，只构建和发布EC2BUnofficialPatch.dll，只注册sa.EC2B.UnofficialPatch一个BepInEx入口。BetterAudio已在1.0.21合并，此后的1.0.23和1.0.23.1继续采用同一布局；不是重新提供独立BA插件。

根工程为EC2BUnofficialPatch.csproj，音频实现在EC2BUnofficialPatch/Features/Audio，由UP运行时统一初始化和清理。旧LFBetterAudio命名空间、类型名及音频补丁ID是兼容实现，不是第二个可单独安装的入口。F9的音频重载由UP提供。

插件版本、文件版本和产品版本为1.0.23.1；程序集绑定身份保留EC2BUnofficialPatch, Version=1.0.9.0。两种版本用途不同，不应为了更新发布号修改外部小游戏适配器的绑定身份。

旧Mod的BetterAudio目录、BetterAudio.json、Timeline和1163指令保持原名。普通数据资源无需因音频合并改名；硬依赖独立BA GUID或程序集的第三方插件须改为依赖UP并重新构建。已安装独立LFBetterAudio.dll的玩家先移走旧副本，避免重复加载。

1.0.24/1.0.25接手测试文件属于历史开发记录，相关音频、视频和更新器改动此前已经回并；这些旧测试版本号高于1.0.23.1，自动更新不执行降级，需手动替换一次。已安装1.0.23的用户按正常版本递增升级。

本版进一步提供属性/特质效果、新前提、社交/空间可见性、控制台历史及地图等扩展，详见[本版更新](../EC2BUnofficialPatch/Docs/CHANGELOG_1.0.23.1.md)。

构建生成本地候选DLL与匹配update.json，Git提交/推送和Release附件上传是独立步骤；详见[自动更新与发布](AUTO_UPDATE.md)和[验证范围](VALIDATION.md)。
