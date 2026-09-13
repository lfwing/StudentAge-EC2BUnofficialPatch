using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using HarmonyLib;

namespace EC2BUnofficialPatch.Core
{
    internal sealed class ModuleHost : IDisposable
    {
        private const string HarmonyRootId = "sa.EC2B.UnofficialPatch";

        private readonly List<ModuleRegistration> _modules = new List<ModuleRegistration>();
        private readonly HashSet<string> _moduleKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<ModuleLogItem> _successfulItems = new List<ModuleLogItem>();
        private readonly HashSet<string> _failedCategories = new HashSet<string>(StringComparer.Ordinal);
        private readonly ManualLogSource _logger;
        private readonly PluginServices _services;
        private bool _disposed;

        internal ModuleHost(ManualLogSource logger, PluginServices services)
        {
            _logger = logger;
            _services = services;
        }

        internal void Load(IPluginModule module)
        {
            if (module == null)
            {
                throw new ArgumentNullException(nameof(module));
            }

            if (!_moduleKeys.Add(module.Key))
            {
                _logger.LogWarning($"模块重复注册，已忽略：key={module.Key}");
                return;
            }

            Harmony harmony = new Harmony($"{HarmonyRootId}.{module.Key}");
            int issueCheckpoint = PatchLog.IssueCount;
            int registrationCheckpoint = PatchLog.RegistrationCheckpoint();
            try
            {
                module.Load(harmony, _services);
                _modules.Add(new ModuleRegistration(module.Key, harmony));
                if (PatchLog.IssueCount == issueCheckpoint)
                {
                    _successfulItems.AddRange(module.LogItems);
                }
                else
                {
                    foreach (ModuleLogItem item in module.LogItems)
                        _failedCategories.Add(item.Category);
                }
            }
            catch (Exception exception)
            {
                try
                {
                    harmony.UnpatchSelf();
                }
                catch
                {
                    // 保留原始加载失败原因。
                }

                string reason = GetReason(exception);
                PatchLog.RollbackRegistrations(registrationCheckpoint);
                _logger.LogError(
                    $"模块加载失败：key={module.Key}, reason={reason}\n{exception}");
                foreach (ModuleLogItem item in module.LogItems)
                {
                    _failedCategories.Add(item.Category);
                    _logger.LogError(
                        $"{item.Category}模块-{item.Feature}未加载：同属模块 {module.Key} 的初始化已中断");
                }

                _moduleKeys.Remove(module.Key);
            }
        }

        internal void LogSelfCheckSummary()
        {
            string[] categoryOrder = { "优化", "屏幕特效", "效果", "机制", "行动指令", "底层服务" };
            IEnumerable<string> categories = categoryOrder
                .Concat(_successfulItems.Select(item => item.Category))
                .Distinct(StringComparer.Ordinal);

            foreach (string category in categories)
            {
                if (_failedCategories.Contains(category))
                    continue;

                string[] features = _successfulItems
                    .Where(item => item.Category == category)
                    .Select(item => item.Feature)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (features.Length == 0)
                    continue;

                _logger.LogInfo($"启动自检通过-{category}模块：{string.Join("、", features)}");
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            for (int index = _modules.Count - 1; index >= 0; index--)
            {
                ModuleRegistration registration = _modules[index];
                try
                {
                    registration.Harmony.UnpatchSelf();
                }
                catch (Exception exception)
                {
                    _logger.LogError(
                        $"模块卸载失败：key={registration.Key}, reason={GetReason(exception)}");
                }
            }

            _modules.Clear();
            _moduleKeys.Clear();
            _successfulItems.Clear();
            _failedCategories.Clear();
        }

        internal static string GetReason(Exception exception)
        {
            Exception current = exception;
            while (current.InnerException != null)
            {
                current = current.InnerException;
            }

            return string.IsNullOrWhiteSpace(current.Message)
                ? current.GetType().Name
                : current.Message.Replace(Environment.NewLine, " ");
        }

        private sealed class ModuleRegistration
        {
            internal ModuleRegistration(string key, Harmony harmony)
            {
                Key = key;
                Harmony = harmony;
            }

            internal string Key { get; }
            internal Harmony Harmony { get; }
        }
    }
}
