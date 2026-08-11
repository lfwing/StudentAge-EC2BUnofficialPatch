using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace EC2BUnofficialPatch.Updater
{
    internal static class Program
    {
        private const string TargetFileName = "EC2BUnofficialPatch.dll";

        private static int Main(string[] args)
        {
            string logPath = null;
            try
            {
                if (args.Length != 6)
                    throw new ArgumentException("参数数量错误");

                int processId = int.Parse(args[0], CultureInfo.InvariantCulture);
                string targetPath = Path.GetFullPath(args[1]);
                string pendingPath = Path.GetFullPath(args[2]);
                string backupPath = Path.GetFullPath(args[3]);
                string expectedHash = args[4];
                logPath = Path.GetFullPath(args[5]);
                ValidatePaths(targetPath, pendingPath, backupPath);
                Log(logPath, $"等待游戏进程退出：pid={processId}, target={targetPath}");
                WaitForExit(processId);

                if (!File.Exists(pendingPath))
                    throw new FileNotFoundException("待安装更新文件不存在", pendingPath);
                string pendingHash = ComputeSha256(pendingPath);
                if (!pendingHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        $"待安装文件 SHA-256 不匹配：expected={expectedHash}, actual={pendingHash}");

                Exception lastError = null;
                for (int attempt = 1; attempt <= 15; attempt++)
                {
                    try
                    {
                        Install(targetPath, pendingPath, backupPath, expectedHash);
                        Log(logPath, $"更新安装成功：target={targetPath}, backup={backupPath}");
                        return 0;
                    }
                    catch (IOException exception)
                    {
                        lastError = exception;
                        Thread.Sleep(1000);
                    }
                    catch (UnauthorizedAccessException exception)
                    {
                        lastError = exception;
                        Thread.Sleep(1000);
                    }
                }

                throw new IOException("多次尝试后仍无法替换插件文件", lastError);
            }
            catch (Exception exception)
            {
                TryLog(logPath, "更新安装失败：" + exception);
                return 1;
            }
        }

        private static void ValidatePaths(string targetPath, string pendingPath, string backupPath)
        {
            if (!Path.GetFileName(targetPath).Equals(TargetFileName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("目标文件名不受允许");
            if (!pendingPath.Equals(targetPath + ".pending", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("待安装文件必须紧邻目标 DLL");
            if (!backupPath.Equals(targetPath + ".backup", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("备份文件路径不受允许");
            if (!string.Equals(
                    Path.GetDirectoryName(targetPath),
                    Path.GetDirectoryName(pendingPath),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新文件与目标文件不在同一目录");
        }

        private static void WaitForExit(int processId)
        {
            if (processId <= 0)
                return;
            try
            {
                using (Process process = Process.GetProcessById(processId))
                    process.WaitForExit();
            }
            catch (ArgumentException)
            {
                // 进程已经退出。
            }
        }

        private static void Install(
            string targetPath,
            string pendingPath,
            string backupPath,
            string expectedHash)
        {
            if (File.Exists(targetPath))
            {
                if (File.Exists(backupPath))
                    File.Delete(backupPath);
                File.Replace(pendingPath, targetPath, backupPath, true);
            }
            else
            {
                File.Move(pendingPath, targetPath);
            }

            string installedHash = ComputeSha256(targetPath);
            if (installedHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                return;

            if (File.Exists(backupPath))
                File.Copy(backupPath, targetPath, true);
            throw new InvalidDataException(
                $"替换后的目标文件校验失败，已尝试回滚：expected={expectedHash}, actual={installedHash}");
        }

        private static string ComputeSha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
        }

        private static void Log(string path, string message)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.AppendAllText(
                path,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) +
                " " + message + Environment.NewLine);
        }

        private static void TryLog(string path, string message)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            try
            {
                Log(path, message);
            }
            catch
            {
            }
        }
    }
}
