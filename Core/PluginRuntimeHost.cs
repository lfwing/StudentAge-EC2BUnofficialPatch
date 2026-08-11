using UnityEngine;

namespace EC2BUnofficialPatch.Core
{
    /// <summary>
    /// 独立于 BepInEx_Manager 的最小持久运行时宿主。
    /// 它只负责跨场景存活并在应用退出时通知统一清理。
    /// </summary>
    internal sealed class PluginRuntimeHost : MonoBehaviour
    {
        internal static PluginRuntimeHost Create()
        {
            GameObject host = new GameObject("EC2BUnofficialPatch_RuntimeHost")
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            DontDestroyOnLoad(host);
            return host.AddComponent<PluginRuntimeHost>();
        }

        private void OnApplicationQuit()
        {
            PluginRuntime.BeginApplicationQuit("RuntimeHost.OnApplicationQuit");
        }

        private void OnDestroy()
        {
            PluginRuntime.NotifyRuntimeHostDestroyed(this);
        }
    }
}
