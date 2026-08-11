using BepInEx;
using EC2BUnofficialPatch.Core;

namespace EC2BUnofficialPatch
{
    [BepInPlugin("sa.EC2B.UnofficialPatch", "EC2BUnofficialPatch", "1.0.16.2")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private int _bootstrapInstanceId;
        private string _bootstrapHostName;

        private void Awake()
        {
            PatchLog.Initialize(Logger);
            PluginConfig.Initialize(Config);
            _bootstrapInstanceId = GetInstanceID();
            _bootstrapHostName = gameObject != null ? gameObject.name : "<null>";

            Logger.LogDebug(
                $"EC2BUnofficialPatch 引导组件启动：version=1.0.16.2, " +
                $"host={_bootstrapHostName}, componentId={_bootstrapInstanceId}");

            PluginRuntime.Start(Logger, _bootstrapHostName, _bootstrapInstanceId);
        }

        private void OnApplicationQuit()
        {
            PluginRuntime.BeginApplicationQuit("BaseUnityPlugin.OnApplicationQuit");
        }

        private void OnDestroy()
        {
            // StudentAge 启动流程会主动销毁挂在 BepInEx_Manager 上的插件组件。
            // 非应用退出阶段绝不能在这里 Dispose/Unpatch，否则所有扩展 EFFECT 会立即失效。
            PluginRuntime.NotifyBootstrapDestroyed(
                _bootstrapHostName,
                _bootstrapInstanceId);
        }
    }
}
