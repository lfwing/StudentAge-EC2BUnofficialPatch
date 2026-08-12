using System;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Security.Cryptography;

namespace EC2BUnofficialPatch.Core.Updates
{
    internal static class EmbeddedUpdater
    {
        private const string ResourceName = "EC2BUnofficialPatch.Embedded.Updater.exe";
        private const string FileName = "EC2BUnofficialPatch.Updater.exe";
        private static readonly object SyncRoot = new object();

        internal static string ExtractNextTo(string pluginPath)
        {
            string pluginDirectory = Path.GetDirectoryName(Path.GetFullPath(pluginPath));
            if (string.IsNullOrWhiteSpace(pluginDirectory))
                throw new InvalidDataException("无法确定当前插件 DLL 所在目录");

            byte[] payload = ReadPayload();
            string hash = ComputeSha256(payload);
            string directory = Path.Combine(
                pluginDirectory,
                ".EC2BUnofficialPatch.Update",
                hash.Substring(0, 16));
            string helperPath = Path.Combine(directory, FileName);

            lock (SyncRoot)
            {
                Directory.CreateDirectory(directory);
                if (File.Exists(helperPath) &&
                    ComputeSha256(File.ReadAllBytes(helperPath)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                {
                    return helperPath;
                }

                string temporaryPath = helperPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllBytes(temporaryPath, payload);
                    if (File.Exists(helperPath))
                        File.Delete(helperPath);
                    File.Move(temporaryPath, helperPath);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }

                return helperPath;
            }
        }

        private static byte[] ReadPayload()
        {
            Assembly assembly = typeof(EmbeddedUpdater).Assembly;
            using (Stream input = assembly.GetManifestResourceStream(ResourceName))
            {
                if (input == null)
                    throw new MissingManifestResourceException("插件内未找到嵌入式更新助手：" + ResourceName);
                using (var output = new MemoryStream())
                {
                    input.CopyTo(output);
                    return output.ToArray();
                }
            }
        }

        private static string ComputeSha256(byte[] data)
        {
            using (SHA256 sha256 = SHA256.Create())
                return BitConverter.ToString(sha256.ComputeHash(data)).Replace("-", string.Empty);
        }
    }
}
