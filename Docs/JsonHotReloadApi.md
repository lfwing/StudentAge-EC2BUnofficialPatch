# JSON 热重载接入规则（1.0.20）

UP 在游戏内监听 `F9`，依次重载普通 Mod `*Cfg.json`、UP 扩展 JSON、BetterAudio.json，
再调用其他插件主动登记的重载提供者。它只刷新 JSON 产生的运行时注册表，不热重载 DLL。

## 其他插件接入

插件引用 `EC2BUnofficialPatch.dll` 后，在自身初始化时登记一个稳定且唯一的提供者 ID：

```csharp
using System;
using EC2BUnofficialPatch.Core;

private IDisposable _jsonHotReload;

private void Awake()
{
    _jsonHotReload = JsonHotReloadApi.Register(
        "author.plugin-name",
        "PluginName.json",
        ReloadJson,
        1000);
}

private JsonHotReloadResult ReloadJson()
{
    // 1. 先发现、读取、解析并校验全部 JSON；此阶段不要修改当前注册表。
    // 2. 全部成功后，用新注册表一次性替换旧注册表。
    return JsonHotReloadResult.Completed(fileCount, entryCount, "注册表已替换");
}

private void OnDestroy()
{
    _jsonHotReload?.Dispose();
}
```

回调会在 Unity 主线程执行。提供者应自行保证“先校验、后替换”；失败时返回
`JsonHotReloadResult.Failed(reason)`，无文件或插件未启用时可返回 `Skip(reason)`。

UP 不会按文件名猜测未知插件的 JSON 语义。遵守该注册规则后，插件以后增加或调整 JSON
无需 UP 再增加特判。UP 自己今后新增的 JSON 模块也必须使用同一注册机制或纳入内置
`up.extension-json` 的事务式注册表构建过程。

## 内置 BetterAudio 兼容

1.0.20 对 `sa.lf.betteraudio` 现有代码提供内置适配：F9 会重新发现并预检全部
`BetterAudio.json`，然后调用 BA 已有的资源包注册入口。不会重新安装 BA 补丁，也不会打断
当前正在播放的音频；下一次解析/播放立即使用新注册表。
