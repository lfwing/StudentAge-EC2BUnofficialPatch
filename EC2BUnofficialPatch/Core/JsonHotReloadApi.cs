using System;
using System.Collections.Generic;
using System.Linq;

namespace EC2BUnofficialPatch.Core
{
    /// <summary>
    /// JSON 热重载提供者的公开结果。其他 BepInEx 插件可以只引用 UP DLL，
    /// 调用 JsonHotReloadApi.Register 注册自己的原子重载回调；UP 无需认识其文件名或格式。
    /// </summary>
    public sealed class JsonHotReloadResult
    {
        private JsonHotReloadResult(bool success, bool skipped, int fileCount, int entryCount, string message)
        {
            Success = success;
            Skipped = skipped;
            FileCount = Math.Max(0, fileCount);
            EntryCount = Math.Max(0, entryCount);
            Message = message ?? string.Empty;
        }

        public bool Success { get; }
        public bool Skipped { get; }
        public int FileCount { get; }
        public int EntryCount { get; }
        public string Message { get; }

        public static JsonHotReloadResult Completed(
            int fileCount,
            int entryCount,
            string message = null) =>
            new JsonHotReloadResult(true, false, fileCount, entryCount, message);

        public static JsonHotReloadResult Skip(string message) =>
            new JsonHotReloadResult(true, true, 0, 0, message);

        public static JsonHotReloadResult Failed(string message) =>
            new JsonHotReloadResult(false, false, 0, 0, message);
    }

    /// <summary>
    /// 跨插件 JSON 热重载注册入口。提供者回调由 UP 在 Unity 主线程上调用，
    /// 回调必须先完整解析/校验，再一次性替换自己的运行时注册表。
    /// </summary>
    public static class JsonHotReloadApi
    {
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, ProviderRegistration> Providers =
            new Dictionary<string, ProviderRegistration>(StringComparer.OrdinalIgnoreCase);

        public static IDisposable Register(
            string providerId,
            string displayName,
            Func<JsonHotReloadResult> reload,
            int order = 1000)
        {
            if (string.IsNullOrWhiteSpace(providerId))
                throw new ArgumentException("providerId 不能为空", nameof(providerId));
            if (reload == null)
                throw new ArgumentNullException(nameof(reload));

            string id = providerId.Trim();
            var registration = new ProviderRegistration(
                id,
                string.IsNullOrWhiteSpace(displayName) ? id : displayName.Trim(),
                order,
                reload);

            lock (SyncRoot)
            {
                if (Providers.ContainsKey(id))
                    throw new InvalidOperationException("JSON 热重载提供者重复注册：" + id);
                Providers[id] = registration;
            }

            return new RegistrationHandle(id, registration);
        }

        internal static IReadOnlyList<ProviderRegistration> Snapshot()
        {
            lock (SyncRoot)
            {
                return Providers.Values
                    .OrderBy(item => item.Order)
                    .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }

        internal static void EnsureBuiltIn(
            string providerId,
            string displayName,
            Func<JsonHotReloadResult> reload,
            int order)
        {
            string id = providerId.Trim();
            lock (SyncRoot)
            {
                if (Providers.TryGetValue(id, out ProviderRegistration existing))
                {
                    if (!existing.BuiltIn)
                        throw new InvalidOperationException("内置 JSON 热重载提供者 ID 已被占用：" + id);
                    existing.Replace(displayName, order, reload);
                    return;
                }

                Providers[id] = new ProviderRegistration(
                    id,
                    displayName,
                    order,
                    reload,
                    true);
            }
        }

        internal sealed class ProviderRegistration
        {
            private Func<JsonHotReloadResult> _reload;

            internal ProviderRegistration(
                string id,
                string displayName,
                int order,
                Func<JsonHotReloadResult> reload,
                bool builtIn = false)
            {
                Id = id;
                DisplayName = displayName;
                Order = order;
                _reload = reload;
                BuiltIn = builtIn;
            }

            internal string Id { get; }
            internal string DisplayName { get; private set; }
            internal int Order { get; private set; }
            internal bool BuiltIn { get; }

            internal JsonHotReloadResult Reload() => _reload();

            internal void Replace(string displayName, int order, Func<JsonHotReloadResult> reload)
            {
                DisplayName = displayName;
                Order = order;
                _reload = reload ?? throw new ArgumentNullException(nameof(reload));
            }
        }

        private sealed class RegistrationHandle : IDisposable
        {
            private readonly string _id;
            private readonly ProviderRegistration _registration;
            private bool _disposed;

            internal RegistrationHandle(string id, ProviderRegistration registration)
            {
                _id = id;
                _registration = registration;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                lock (SyncRoot)
                {
                    if (Providers.TryGetValue(_id, out ProviderRegistration current) &&
                        ReferenceEquals(current, _registration))
                    {
                        Providers.Remove(_id);
                    }
                }
            }
        }
    }
}
