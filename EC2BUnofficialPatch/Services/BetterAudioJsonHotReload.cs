using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using EC2BUnofficialPatch.Core;
using Newtonsoft.Json.Linq;

namespace EC2BUnofficialPatch.Services
{
    /// <summary>
    /// UP 内置音频 JSON 热重载。只重跑其 JSON 资源包发现/注册逻辑，
    /// 不重建 Harmony 补丁、播放器或当前正在播放的音频。
    /// </summary>
    internal static class BetterAudioJsonHotReload
    {
        private static readonly object SyncRoot = new object();
        private static Dictionary<string, string> _baseline;
        private static bool _baselineAttempted;

        internal static void EnsureBaseline()
        {
            lock (SyncRoot)
            {
                if (_baseline != null || _baselineAttempted ||
                    LFBetterAudio.Plugin.ConfigStore == null)
                    return;
                _baselineAttempted = true;
            }

            try
            {
                BetterAudioContext context = ResolveContext();
                object discovered = context.Discover.Invoke(null, new object[] { Paths.GameRootPath });
                Dictionary<string, string> snapshot = BuildPackageSnapshot(discovered as IEnumerable, false);
                lock (SyncRoot)
                    _baseline = snapshot;
            }
            catch
            {
                // 真正按下快捷键时再输出完整兼容错误，避免启动阶段制造无关日志。
            }
        }

        internal static JsonHotReloadResult Reload()
        {
            if (LFBetterAudio.Plugin.ConfigStore == null)
                return JsonHotReloadResult.Skip("UP 音频演出尚未初始化");

            try
            {
                BetterAudioContext context = ResolveContext();
                object discovered = context.Discover.Invoke(null, new object[] { Paths.GameRootPath });
                Dictionary<string, string> current = BuildPackageSnapshot(discovered as IEnumerable, true);
                string[] changed;
                lock (SyncRoot)
                {
                    if (_baseline == null)
                    {
                        _baseline = current;
                        return JsonHotReloadResult.Skip("BetterAudio JSON 已建立基线");
                    }
                    changed = FindChanges(_baseline, current);
                }
                if (changed.Length == 0)
                    return JsonHotReloadResult.Skip("BetterAudio JSON 未变化");

                object store = context.Reload(discovered);
                int entryCount = ReadIntProperty(store, "Count");
                int packageCount = ReadIntProperty(store, "PackageCount");
                lock (SyncRoot)
                {
                    _baseline = current;
                    _baselineAttempted = true;
                }
                return JsonHotReloadResult.Completed(
                    changed.Length,
                    entryCount,
                    $"BetterAudio变更={DescribePaths(changed)}，资源包={packageCount}；当前播放不打断");
            }
            catch (TargetInvocationException exception)
            {
                return JsonHotReloadResult.Failed(DescribeError(exception));
            }
            catch (Exception exception)
            {
                return JsonHotReloadResult.Failed(DescribeError(exception));
            }
        }

        private static BetterAudioContext ResolveContext()
        {
            Type pluginType = typeof(LFBetterAudio.Plugin);
            MethodInfo discover = pluginType.Assembly
                .GetType("LFBetterAudio.Discovery.BetterAudioPackageDiscovery", true)
                .GetMethod(
                    "DiscoverWorkshopPackages",
                    BindingFlags.Static | BindingFlags.Public,
                    null,
                    new[] { typeof(string) },
                    null);
            PropertyInfo configStore = pluginType.GetProperty(
                "ConfigStore",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo loadAll = configStore?.PropertyType.GetMethod(
                "LoadAll",
                BindingFlags.Static | BindingFlags.Public,
                null,
                new[] { discover?.ReturnType },
                null);
            if (loadAll == null && configStore != null)
            {
                loadAll = configStore.PropertyType.GetMethods(BindingFlags.Static | BindingFlags.Public)
                    .FirstOrDefault(method =>
                        method.Name == "LoadAll" && method.GetParameters().Length == 1);
            }
            MethodInfo setConfigStore = configStore?.GetSetMethod(true);
            if (discover == null || configStore == null || loadAll == null || setConfigStore == null)
                throw new MissingMemberException("BetterAudio 版本结构不兼容：找不到 JSON 注册入口");
            return new BetterAudioContext(discover, loadAll, configStore, setConfigStore);
        }

        private static Dictionary<string, string> BuildPackageSnapshot(
            IEnumerable packages,
            bool validateSyntax)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (packages == null)
                return result;

            foreach (object package in packages)
            {
                if (package == null)
                    continue;
                PropertyInfo pathProperty = package.GetType().GetProperty(
                    "ConfigPath",
                    BindingFlags.Instance | BindingFlags.Public);
                string path = pathProperty?.GetValue(package, null) as string;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    continue;

                try
                {
                    string content = File.ReadAllText(path);
                    if (validateSyntax)
                        JToken.Parse(content);
                    using (SHA256 sha = SHA256.Create())
                    {
                        result[Path.GetFullPath(path)] = Convert.ToBase64String(
                            sha.ComputeHash(Encoding.UTF8.GetBytes(content)));
                    }
                }
                catch (Exception exception)
                {
                    throw new InvalidDataException(
                        $"BetterAudio JSON 解析失败：file={path}, reason={ModuleHost.GetReason(exception)}",
                        exception);
                }
            }

            return result;
        }

        private static string[] FindChanges(
            IReadOnlyDictionary<string, string> previous,
            IReadOnlyDictionary<string, string> current)
        {
            return current.Keys
                .Where(path => !previous.TryGetValue(path, out string hash) || hash != current[path])
                .Concat(previous.Keys.Where(path => !current.ContainsKey(path)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string DescribePaths(IReadOnlyList<string> paths)
        {
            string[] shown = paths.Take(8).ToArray();
            string suffix = paths.Count > shown.Length ? $"，另有{paths.Count - shown.Length}个" : string.Empty;
            return string.Join("；", shown) + suffix;
        }

        private static string DescribeError(Exception exception)
        {
            Exception current = exception is TargetInvocationException && exception.InnerException != null
                ? exception.InnerException
                : exception;
            return string.IsNullOrWhiteSpace(current.Message)
                ? current.GetType().Name
                : current.Message.Replace(Environment.NewLine, " ");
        }

        private static int ReadIntProperty(object instance, string propertyName)
        {
            object value = instance?.GetType()
                .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(instance, null);
            return value is int number ? number : 0;
        }

        private sealed class BetterAudioContext
        {
            internal BetterAudioContext(
                MethodInfo discover,
                MethodInfo loadAll,
                PropertyInfo configStore,
                MethodInfo setConfigStore)
            {
                Discover = discover;
                LoadAll = loadAll;
                ConfigStore = configStore;
                SetConfigStore = setConfigStore;
            }

            internal MethodInfo Discover { get; }
            internal MethodInfo LoadAll { get; }
            internal PropertyInfo ConfigStore { get; }
            internal MethodInfo SetConfigStore { get; }

            internal object Reload(object discovered)
            {
                object store = LoadAll.Invoke(null, new[] { discovered });
                if (store == null)
                    throw new InvalidOperationException("BetterAudio ConfigStore.LoadAll 返回空结果");
                SetConfigStore.Invoke(null, new[] { store });
                return store;
            }
        }
    }
}
