using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using Newtonsoft.Json;

namespace EC2BUnofficialPatch.Core.Updates
{
    internal static class UpdateService
    {
        private const int ManifestLimit = 256 * 1024;
        private const int PackageLimit = 32 * 1024 * 1024;
        private const string AssetName = "EC2BUnofficialPatch.dll";
        private const string DefaultReleaseManifest =
            "https://github.com/lfwing/StudentAge-EC2BUnofficialPatch/releases/latest/download/update.json";
        private const string DefaultRawManifest =
            "https://raw.githubusercontent.com/lfwing/StudentAge-EC2BUnofficialPatch/main/update.json";

        private static readonly object SyncRoot = new object();
        private static CancellationTokenSource _cancellation;
        private static bool _started;
        private static bool _helperStarted;

        internal static void Start()
        {
            lock (SyncRoot)
            {
                if (_started || PluginConfig.UpdateAutoCheck?.Value != true)
                    return;

                _started = true;
                _cancellation = new CancellationTokenSource();
                CancellationToken token = _cancellation.Token;
                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
                        CheckForUpdates(token);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception exception)
                    {
                        PatchLog.Warning(
                            "更新模块-自动更新检查异常，插件本体继续运行：" +
                            ModuleHost.GetReason(exception));
                    }
                }, token);
            }
        }

        internal static void Stop()
        {
            lock (SyncRoot)
            {
                _cancellation?.Cancel();
                _cancellation?.Dispose();
                _cancellation = null;
                _started = false;
            }
        }

        private static void CheckForUpdates(CancellationToken token)
        {
            string statePath = Path.Combine(Paths.ConfigPath, "EC2BUnofficialPatch.update-state.json");
            UpdateState state = LoadState(statePath);
            if (!ShouldCheck(state))
            {
                PatchLog.Debug("更新模块-尚未达到检查间隔，本次启动跳过联网检查");
                return;
            }

            state.lastAttemptUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            UpdateManifest manifest = null;
            string manifestUrl = null;
            var failures = new List<string>();
            foreach (string candidate in GetManifestUrls())
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    byte[] data = Download(candidate, ManifestLimit, 7000, token);
                    string json = new UTF8Encoding(false, true).GetString(data);
                    UpdateManifest parsed = JsonConvert.DeserializeObject<UpdateManifest>(json);
                    ValidateManifest(parsed);
                    manifest = parsed;
                    manifestUrl = candidate;
                    break;
                }
                catch (Exception exception)
                {
                    failures.Add(candidate + " => " + ModuleHost.GetReason(exception));
                }
            }

            if (manifest == null)
            {
                SaveState(statePath, state);
                PatchLog.Warning(
                    "更新模块-所有更新清单均不可用，插件本体继续运行：" +
                    string.Join(" | ", failures));
                return;
            }

            state.lastManifestUrl = manifestUrl;
            state.lastSeenVersion = manifest.version;
            Version current = ParseVersion(PluginMetadata.Version, "本地插件版本");
            Version latest = ParseVersion(manifest.version, "远程插件版本");
            if (latest <= current)
            {
                SaveState(statePath, state);
                PatchLog.Info(
                    $"更新模块-版本检查完成：current={PluginMetadata.Version}, latest={manifest.version}, result=已是最新版本");
                return;
            }

            if (PluginConfig.UpdateAutoInstall?.Value != true)
            {
                SaveState(statePath, state);
                PatchLog.Warning(
                    $"更新模块-发现新版本：current={PluginMetadata.Version}, latest={manifest.version}, " +
                    $"自动安装已关闭，下载页={manifest.releasePage ?? PluginMetadata.Repository + "/releases/latest"}");
                return;
            }

            string assemblyPath = Path.GetFullPath(typeof(UpdateService).Assembly.Location);
            if (!string.Equals(Path.GetFileName(assemblyPath), AssetName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("当前插件程序集文件名不是 " + AssetName + "，拒绝自动替换");

            string pendingPath = assemblyPath + ".pending";
            DownloadPackage(manifest, pendingPath, token);
            string helperPath = Path.Combine(
                Paths.PluginPath,
                "EC2BUnofficialPatch",
                "Updater",
                "EC2BUnofficialPatch.Updater.exe");
            if (!File.Exists(helperPath))
            {
                PatchLog.Error(
                    $"更新模块-更新文件已校验但缺少替换助手，无法自动安装：helper={helperPath}, pending={pendingPath}");
                SaveState(statePath, state);
                return;
            }

            if (!StartHelper(helperPath, assemblyPath, pendingPath, manifest.sha256))
            {
                SaveState(statePath, state);
                return;
            }

            state.scheduledVersion = manifest.version;
            SaveState(statePath, state);
            PatchLog.Info(
                $"更新模块-新版本已下载并校验，将在游戏退出后安装：" +
                $"current={PluginMetadata.Version}, latest={manifest.version}, source={manifestUrl}");
        }

        private static bool ShouldCheck(UpdateState state)
        {
            int hours = PluginConfig.UpdateCheckIntervalHours?.Value ?? 24;
            hours = Math.Max(1, Math.Min(168, hours));
            if (state == null || string.IsNullOrWhiteSpace(state.lastAttemptUtc))
                return true;
            if (!DateTime.TryParse(
                    state.lastAttemptUtc,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTime last))
                return true;
            return DateTime.UtcNow - last >= TimeSpan.FromHours(hours);
        }

        private static IEnumerable<string> GetManifestUrls()
        {
            var urls = new List<string>();
            string mirrors = PluginConfig.UpdateManifestMirrors?.Value;
            if (!string.IsNullOrWhiteSpace(mirrors))
            {
                urls.AddRange(mirrors.Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
            }
            urls.Add(DefaultReleaseManifest);
            urls.Add(DefaultRawManifest);
            return urls.Select(url => url.Trim())
                .Where(url => IsHttps(url))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static void ValidateManifest(UpdateManifest manifest)
        {
            if (manifest == null || manifest.schema != 1)
                throw new InvalidDataException("清单 schema 不是受支持的版本 1");
            ParseVersion(manifest.version, "清单 version");
            if (!string.Equals(manifest.channel, "stable", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("清单 channel 不是 stable");
            if (!string.Equals(manifest.assetName, AssetName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("清单 assetName 不匹配");
            if (manifest.size <= 0 || manifest.size > PackageLimit)
                throw new InvalidDataException("清单 size 超出允许范围");
            if (!IsSha256(manifest.sha256))
                throw new InvalidDataException("清单 sha256 不是 64 位十六进制值");
            if (manifest.downloadUrls == null || manifest.downloadUrls.Count == 0 ||
                manifest.downloadUrls.Any(url => !IsHttps(url)))
                throw new InvalidDataException("清单 downloadUrls 必须全部为 HTTPS 地址");
        }

        private static void DownloadPackage(UpdateManifest manifest, string pendingPath, CancellationToken token)
        {
            var failures = new List<string>();
            foreach (string url in manifest.downloadUrls.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    byte[] data = Download(url, PackageLimit, 45000, token);
                    if (data.LongLength != manifest.size)
                        throw new InvalidDataException($"文件大小不匹配：expected={manifest.size}, actual={data.LongLength}");
                    string actualHash = ComputeSha256(data);
                    if (!actualHash.Equals(manifest.sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"SHA-256 不匹配：expected={manifest.sha256}, actual={actualHash}");

                    File.WriteAllBytes(pendingPath, data);
                    return;
                }
                catch (Exception exception)
                {
                    failures.Add(url + " => " + ModuleHost.GetReason(exception));
                }
            }
            throw new IOException("全部更新下载源均失败：" + string.Join(" | ", failures));
        }

        private static byte[] Download(string url, int maxBytes, int timeoutMs, CancellationToken token)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.UserAgent = PluginMetadata.Name + "/" + PluginMetadata.Version;
            request.Accept = "application/json, application/octet-stream;q=0.9, */*;q=0.8";
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.AllowAutoRedirect = true;
            request.MaximumAutomaticRedirections = 5;
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;

            using (token.Register(request.Abort))
            using (var response = (HttpWebResponse)request.GetResponse())
            {
                if (response.ContentLength > maxBytes)
                    throw new InvalidDataException("远程文件超过大小限制");
                using (Stream input = response.GetResponseStream())
                using (var output = new MemoryStream())
                {
                    var buffer = new byte[81920];
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        if (output.Length + read > maxBytes)
                            throw new InvalidDataException("下载内容超过大小限制");
                        output.Write(buffer, 0, read);
                    }
                    return output.ToArray();
                }
            }
        }

        private static bool StartHelper(string helperPath, string targetPath, string pendingPath, string sha256)
        {
            lock (SyncRoot)
            {
                if (_helperStarted)
                    return true;

                try
                {
                    string backupPath = targetPath + ".backup";
                    string logPath = Path.Combine(Paths.ConfigPath, "EC2BUnofficialPatch.update.log");
                    string arguments = string.Join(" ", new[]
                    {
                        Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture),
                        Quote(targetPath),
                        Quote(pendingPath),
                        Quote(backupPath),
                        sha256,
                        Quote(logPath)
                    });
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = helperPath,
                        Arguments = arguments,
                        WorkingDirectory = Path.GetDirectoryName(helperPath),
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    });
                    _helperStarted = true;
                    return true;
                }
                catch (Exception exception)
                {
                    PatchLog.Error(
                        "更新模块-无法启动退出后替换助手：" + ModuleHost.GetReason(exception));
                    return false;
                }
            }
        }

        private static UpdateState LoadState(string path)
        {
            try
            {
                return File.Exists(path)
                    ? JsonConvert.DeserializeObject<UpdateState>(File.ReadAllText(path)) ?? new UpdateState()
                    : new UpdateState();
            }
            catch
            {
                return new UpdateState();
            }
        }

        private static void SaveState(string path, UpdateState state)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonConvert.SerializeObject(state, Formatting.Indented), new UTF8Encoding(false));
            }
            catch (Exception exception)
            {
                PatchLog.Warning("更新模块-无法保存检查状态：" + ModuleHost.GetReason(exception));
            }
        }

        private static Version ParseVersion(string value, string field)
        {
            string normalized = value?.Trim();
            if (!string.IsNullOrEmpty(normalized) && (normalized[0] == 'v' || normalized[0] == 'V'))
                normalized = normalized.Substring(1);
            int suffix = normalized?.IndexOfAny(new[] { '-', '+' }) ?? -1;
            if (suffix >= 0)
                normalized = normalized.Substring(0, suffix);
            if (!Version.TryParse(normalized, out Version version))
                throw new InvalidDataException(field + " 无法解析：" + (value ?? "<null>"));
            return version;
        }

        private static string ComputeSha256(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", string.Empty);
        }

        private static bool IsSha256(string value) =>
            value != null && value.Length == 64 && value.All(Uri.IsHexDigit);

        private static bool IsHttps(string value) =>
            Uri.TryCreate(value, UriKind.Absolute, out Uri uri) &&
            uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

        private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
    }
}
