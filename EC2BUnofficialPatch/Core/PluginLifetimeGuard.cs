using UnityEngine;
using UnityEngine.SceneManagement;

namespace EC2BUnofficialPatch.Core
{
    internal sealed class PluginLifetimeGuard
    {
        private readonly GameObject _host;

        internal PluginLifetimeGuard(GameObject host)
        {
            _host = host;
            _host.name = "EC2BUnofficialPatch_PersistentHost";
            Object.DontDestroyOnLoad(_host);
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            PatchLog.Debug($"底层服务模块-保活补丁已启用：host={_host.name}, instanceId={_host.GetInstanceID()}");
        }

        internal bool IsAlive => _host != null;

        internal void Dispose()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) =>
            PatchLog.Debug($"底层服务模块-场景加载：scene={scene.name}, mode={mode}, hostAlive={IsAlive}");

        private void OnSceneUnloaded(Scene scene) =>
            PatchLog.Debug($"底层服务模块-场景卸载：scene={scene.name}, hostAlive={IsAlive}");

        private void OnActiveSceneChanged(Scene oldScene, Scene newScene) =>
            PatchLog.Debug($"底层服务模块-活动场景切换：{oldScene.name} -> {newScene.name}, hostAlive={IsAlive}");
    }
}
