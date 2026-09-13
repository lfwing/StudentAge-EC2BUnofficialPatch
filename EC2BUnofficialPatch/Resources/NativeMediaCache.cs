using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using EC2BUnofficialPatch.Core;

namespace EC2BUnofficialPatch.Resources
{
    /// <summary>
    /// 为不能可靠处理 Unicode/URI 路径的 Unity 原生媒体后端提供纯 ASCII 文件名副本。
    /// 仅在直接播放失败后调用，不改变原资源，也不参与普通图片和 JSON 的读取。
    /// </summary>
    internal static class NativeMediaCache
    {
        private static readonly object SyncRoot = new object();

        internal static bool TryCreateFallback(
            string sourcePath,
            out string cachedPath,
            out string reason)
        {
            cachedPath = null;
            reason = null;

            try
            {
                string source = Path.GetFullPath(sourcePath ?? string.Empty);
                FileInfo file = new FileInfo(source);
                if (!file.Exists)
                {
                    reason = "源文件不存在";
                    return false;
                }

                string extension = Path.GetExtension(source).ToLowerInvariant();
                string fileName;
                using (SHA256 sha256 = SHA256.Create())
                {
                    byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(source.ToUpperInvariant()));
                    fileName = BitConverter.ToString(hash, 0, 16).Replace("-", string.Empty).ToLowerInvariant() + extension;
                }

                string directory = Path.Combine(Paths.CachePath, PluginMetadata.Name, "NativeMedia");
                cachedPath = Path.Combine(directory, fileName);
                lock (SyncRoot)
                {
                    Directory.CreateDirectory(directory);
                    if (File.Exists(cachedPath))
                    {
                        FileInfo cached = new FileInfo(cachedPath);
                        if (cached.Length == file.Length && cached.LastWriteTimeUtc == file.LastWriteTimeUtc)
                        {
                            return true;
                        }
                    }

                    string temporaryPath = cachedPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        File.Copy(source, temporaryPath, true);
                        File.SetLastWriteTimeUtc(temporaryPath, file.LastWriteTimeUtc);
                        if (File.Exists(cachedPath))
                        {
                            File.Delete(cachedPath);
                        }

                        File.Move(temporaryPath, cachedPath);
                    }
                    finally
                    {
                        if (File.Exists(temporaryPath))
                        {
                            File.Delete(temporaryPath);
                        }
                    }

                    return true;
                }
            }
            catch (Exception exception)
            {
                cachedPath = null;
                reason = exception.Message;
                return false;
            }
        }
    }
}
