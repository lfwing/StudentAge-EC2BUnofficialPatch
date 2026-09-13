# StudentAge UP + BetterAudio

当前源码版本：1.0.25。基于 UP 1.0.21 工作副本与 BetterAudio 1.0.0，原作者 lfw/lfwing，雾雁参与开发。保留原 LICENSE 和来源，不改写原作者归属。

## 安装方式

需要学生时代 Windows x64 Mono 版与 BepInEx 5。

- merged：一个 EC2BUnofficialPatch.dll 包含 UP/BA 两个逻辑入口。
- split：EC2BUnofficialPatch.dll 与 LFBetterAudio.dll 分开。依赖 LFBetterAudio 程序集名称的第三方插件使用此方式。

两种方式二选一，安装时先退出游戏并移出旧的同 GUID 插件副本。保留原配置和 Mod 资源。UP AssemblyVersion 保持1.0.9.0，BA入口版本保持1.0.0，旧GUID/公开接口/JSON路径兼容。建议 BepInEx 的 HideManagerGameObject=true。

原 UP 源码整理到 EC2BUnofficialPatch/，BA 源码位于 LFBetterAudio/。对应功能用法见两个目录中的 README.md。原命名空间不变。

## 合并适配与修复

- BA Harmony扫描只处理自身命名空间的补丁，音频控制器使用持久宿主；UP软依赖BA以保持加载顺序。
- 音频追踪按实际调用类型识别BA，兼容同程序集；路径以AudioClip弱键保存，重复日志使用有限帧窗口。
- 资料页静态Mod立绘按当前服装与学段urlParm显示，切回原版角色恢复布局，异步旧请求不会覆盖新角色；修复缺失差分时小学/中学图片回退取反。
- 外部小游戏接口增加Begin/Cancel/Invalidated，支持准备阶段延迟扣费，统一NPC阶段和单次结算，阻止重复启动及过期世界结算。本仓库提供导入接口，玩法由独立插件实现。
- 地图外置Live2D使用游戏自带Core/SDK；保持地图范围和失败回退，不扩展为通用Talk动画加载器。

## 在线更新

沿用原 UP schema1、内嵌助手、退出替换和备份回滚，仅增加layout标记。

- merged读取update-merged.json，split读取原update.json。
- 类型不匹配或未标记的清单会跳过；不让旧UP覆盖含BA的DLL。
- split只更新UP，现有BA保持原样；Workshop实际插件来源路径可更新。

详见 [发布说明](docs/AUTO_UPDATE.md)。本次代码提交保留根update.json的已发布版本；release-manifests/是待发布清单。发布配套DLL后才替换公开清单，避免玩家下载尚不存在的版本。

## 构建

依赖Python3、.NET SDK、本机游戏Managed与BepInEx/core；不分发游戏依赖。

```sh
python3 build.py --game "游戏目录" --bepinex "BepInEx/core目录" --layout all
```

输出dist/merged和dist/split；prepare_release.py生成两份schema1清单与上传资产，不执行上传。

也可用标准工程：

```sh
dotnet build StudentAge.Merged.csproj -c Release /p:GameDir="游戏目录" /p:BepInExCoreDir="BepInEx/core目录"
```

更新器的定向检查（.NET10，无需启动游戏）：

```sh
dotnet run --project tools/UpdateTests.csproj
```

验证范围见 [VALIDATION](docs/VALIDATION.md)。游戏资源、私人存档和反编译游戏源码不包含在本仓库。
