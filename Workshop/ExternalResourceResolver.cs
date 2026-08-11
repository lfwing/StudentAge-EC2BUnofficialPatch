using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sdk.PlatformAPI;

namespace EC2BUnofficialPatch.Workshop
{
    /// <summary>
    /// 游戏原版 Mods/&lt;packageId&gt;/... 逻辑路径的解析结果。
    /// packageId 对应 ModMetadata.packageId：它不是 Steam Workshop 数字 ID，也不是 Workshop 安装目录中的物理子文件夹。
    /// 原版 ModCtrl.GetFullUrl 会先用 packageId -> PublishedFileId 找到 Workshop 安装根，再丢弃 Mods/packageId 前缀，
    /// 将 ContentRelativePath 拼到该 Workshop 根目录下。
    /// </summary>
    internal sealed class GameModResourcePath
    {
        internal GameModResourcePath(string packageId, string contentRelativePath)
        {
            PackageId = packageId;
            ContentRelativePath = contentRelativePath;
        }

        internal string PackageId { get; }
        internal string ContentRelativePath { get; }
        internal bool IsModQualified => !string.IsNullOrWhiteSpace(PackageId);
    }

    /// <summary>
    /// EC2BUnofficialPatch 外置资源统一路径规则。
    ///
    /// 统一负责：
    /// 1. 路径斜杠与相对路径安全校验；
    /// 2. 解析游戏原版 Mods/&lt;packageId&gt;/... 逻辑 URL；
    /// 3. 将 ModMetadata.packageId 映射到 Steam PublishedFileId / Workshop 内容根；
    /// 4. 按目录名嗅探功能资源根（例如 comic）；
    /// 5. 从功能锚点之后提取真正的资源相对路径；
    /// 6. 图片/视频扩展名白名单。
    ///
    /// 注意：Mods/&lt;packageId&gt;/... 是游戏逻辑路径，不代表物理目录必须存在 Mods/&lt;packageId&gt;。
    /// 具体功能仍负责自己的语义校验（例如漫画 {图号}-{分镜号}）。
    /// </summary>
    internal sealed class ExternalResourceResolver
    {
        internal static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg" };
        internal static readonly string[] VideoExtensions = { ".mp4", ".webm", ".mov", ".m4v", ".ogv" };

        private readonly ContentRootCatalog _catalog;

        internal ExternalResourceResolver(ContentRootCatalog catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        internal bool ContainsRoot(string rootId)
        {
            return _catalog.Roots.Any(root => string.Equals(root.Id, rootId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 资源索引在插件启动时会看到所有已订阅 Workshop 目录；真正解析时优先只考虑当前游戏启用的 Mod。
        /// activeMods 尚未初始化时保持兼容，暂不做过滤。插件自身本地资源始终允许。
        /// </summary>
        internal bool IsRuntimeRootEligible(string rootId)
        {
            if (string.IsNullOrWhiteSpace(rootId) || string.Equals(rootId, "plugin-local", StringComparison.OrdinalIgnoreCase))
                return true;
            if (!rootId.StartsWith("workshop-", StringComparison.OrdinalIgnoreCase))
                return true;

            try
            {
                List<ulong> activeMods = ModCtrl.Ins?.activeMods;
                if (activeMods == null || activeMods.Count == 0)
                    return true;

                string idText = rootId.Substring("workshop-".Length);
                return ulong.TryParse(idText, out ulong publishedFileId) && activeMods.Contains(publishedFileId);
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// 按游戏原版语义，将 ModMetadata.packageId 映射成 ContentRootCatalog 使用的 workshop-&lt;PublishedFileId&gt;。
        /// packageId 不是 Workshop 数字 ID。若同一个 packageId 被多个已加载 Mod 重复声明，拒绝任意选择。
        /// </summary>
        internal bool TryResolveWorkshopRootIdByPackageId(
            string packageId,
            out string rootId,
            out string reason)
        {
            rootId = null;
            reason = null;
            if (string.IsNullOrWhiteSpace(packageId))
            {
                reason = "packageId 为空";
                return false;
            }

            try
            {
                Dictionary<ulong, ModMetadata> metadatas = ModCtrl.Ins?.modMetadatas;
                if (metadatas == null || metadatas.Count == 0)
                {
                    reason = $"游戏尚未取得 Mod metadata，无法解析 packageId={packageId}";
                    return false;
                }

                KeyValuePair<ulong, ModMetadata>[] matches = metadatas
                    .Where(pair => string.Equals(pair.Value?.packageId, packageId, StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                if (matches.Length == 0)
                {
                    reason = $"没有已加载 Mod 声明 packageId={packageId}";
                    return false;
                }

                if (matches.Length > 1)
                {
                    reason = $"packageId={packageId} 被 {matches.Length} 个 Workshop Mod 重复声明，拒绝任意选择";
                    return false;
                }

                rootId = "workshop-" + matches[0].Key;
                if (!ContainsRoot(rootId))
                {
                    reason = $"packageId={packageId} 已映射到 Workshop ID={matches[0].Key}，但资源索引中没有内容根 {rootId}";
                    rootId = null;
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                reason = $"读取 Mod metadata 失败：{exception.Message}";
                rootId = null;
                return false;
            }
        }

        internal static string NormalizeRelativePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            string raw = value.Trim();
            if (raw.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("\\\\", StringComparison.Ordinal) ||
                raw.StartsWith("//", StringComparison.Ordinal) ||
                (raw.Length >= 3 && char.IsLetter(raw[0]) && raw[1] == ':' && (raw[2] == '\\' || raw[2] == '/')))
            {
                return null; // CFG 外置资源只接受可移植逻辑/相对路径，不接受绝对路径或 file:// URI。
            }

            string normalized = raw.Replace('\\', '/').TrimStart('/');
            string[] segments = normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0 || segments.Any(segment => segment == "." || segment == ".."))
                return null;

            return string.Join("/", segments);
        }

        internal static string StripPrefixes(string value, params string[] prefixes)
        {
            string normalized = NormalizeRelativePath(value);
            if (normalized == null)
                return null;

            bool stripped;
            do
            {
                stripped = false;
                foreach (string rawPrefix in prefixes ?? Array.Empty<string>())
                {
                    string prefix = NormalizeRelativePath(rawPrefix);
                    if (string.IsNullOrEmpty(prefix))
                        continue;
                    prefix = prefix.TrimEnd('/') + "/";

                    if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    normalized = normalized.Substring(prefix.Length).TrimStart('/');
                    stripped = true;
                    break;
                }
            }
            while (stripped && normalized.Length > 0);

            return NormalizeRelativePath(normalized);
        }

        /// <summary>
        /// 解析游戏原版逻辑 URL：Mods/&lt;packageId&gt;/&lt;Workshop 根内相对路径&gt;。
        /// 例如 Mods/hengwuyuan/Textures/Bg/a.png：
        /// packageId=hengwuyuan，ContentRelativePath=Textures/Bg/a.png。
        /// 物理路径最终是 &lt;对应 Workshop 安装根&gt;/Textures/Bg/a.png，而不是
        /// &lt;Workshop 安装根&gt;/Mods/hengwuyuan/Textures/Bg/a.png。
        /// 非 Mods 路径会作为未限定来源的相对路径返回。
        /// </summary>
        internal static bool TryParseGameModResourcePath(string value, out GameModResourcePath result)
        {
            result = null;
            string normalized = NormalizeRelativePath(value);
            if (normalized == null)
                return false;

            string[] segments = normalized.Split('/');
            if (!string.Equals(segments[0], "Mods", StringComparison.OrdinalIgnoreCase))
            {
                result = new GameModResourcePath(null, normalized);
                return true;
            }

            // 一旦明确写了 Mods，就必须满足原版 Mods/<packageId>/<resource...> 结构；
            // 不把残缺的 Mods 路径降级为“无来源短路径”，避免串资源。
            if (segments.Length < 3 || string.IsNullOrWhiteSpace(segments[1]))
                return false;

            string remainder = NormalizeRelativePath(string.Join("/", segments.Skip(2)));
            if (remainder == null)
                return false;

            result = new GameModResourcePath(segments[1], remainder);
            return true;
        }

        /// <summary>
        /// 如果路径中含指定功能目录名，取最后一个该目录之后的部分。
        /// 例如 EC2BUnofficialPatch/LoveDraw/a/b.png -> a/b.png。
        /// 不含锚点则原样返回，允许 CFG 直接写相对于功能目录的路径。
        /// </summary>
        internal static string ExtractAfterAnchor(string value, string anchorDirectoryName)
        {
            string normalized = NormalizeRelativePath(value);
            if (normalized == null || string.IsNullOrWhiteSpace(anchorDirectoryName))
                return normalized;

            string[] segments = normalized.Split('/');
            int anchorIndex = -1;
            for (int i = 0; i < segments.Length; i++)
            {
                if (string.Equals(segments[i], anchorDirectoryName, StringComparison.OrdinalIgnoreCase))
                    anchorIndex = i;
            }

            if (anchorIndex < 0)
                return normalized;
            if (anchorIndex >= segments.Length - 1)
                return null;

            return NormalizeRelativePath(string.Join("/", segments.Skip(anchorIndex + 1)));
        }

        internal static bool HasSupportedExtension(string path, IEnumerable<string> extensions)
        {
            string extension = Path.GetExtension(path ?? string.Empty);
            return (extensions ?? Array.Empty<string>()).Any(item =>
                string.Equals(item, extension, StringComparison.OrdinalIgnoreCase));
        }

        internal static string RemoveSupportedExtension(string path, IEnumerable<string> extensions)
        {
            string normalized = NormalizeRelativePath(path);
            if (normalized == null)
                return null;

            string extension = Path.GetExtension(normalized);
            if (!(extensions ?? Array.Empty<string>()).Any(item =>
                string.Equals(item, extension, StringComparison.OrdinalIgnoreCase)))
            {
                return normalized;
            }

            return normalized.Substring(0, normalized.Length - extension.Length);
        }

        /// <summary>
        /// 安全递归寻找名字恰好匹配的目录。stopAtMatch=true 时找到功能根后不再继续深入，
        /// 避免父/子同名目录导致同一资源被重复索引。
        /// </summary>
        internal static IEnumerable<string> FindDirectoriesNamed(
            string rootPath,
            string directoryName,
            bool stopAtMatch,
            Action<string, Exception> onDirectoryError = null)
        {
            if (!Directory.Exists(rootPath) || string.IsNullOrWhiteSpace(directoryName))
                yield break;

            Queue<string> pending = new Queue<string>();
            pending.Enqueue(rootPath);

            while (pending.Count > 0)
            {
                string current = pending.Dequeue();
                string[] children;
                try
                {
                    children = Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly)
                        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                }
                catch (Exception exception)
                {
                    onDirectoryError?.Invoke(current, exception);
                    continue;
                }

                foreach (string child in children)
                {
                    bool match = string.Equals(Path.GetFileName(child), directoryName, StringComparison.OrdinalIgnoreCase);
                    if (match)
                        yield return child;
                    if (!match || !stopAtMatch)
                        pending.Enqueue(child);
                }
            }
        }

        internal static string FindChildDirectory(string parent, string name)
        {
            if (!Directory.Exists(parent))
                return null;

            string exact = Path.Combine(parent, name);
            if (Directory.Exists(exact))
                return exact;

            try
            {
                return Directory.EnumerateDirectories(parent, "*", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(path => string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }

        internal static string FindChildFile(string parent, string name)
        {
            if (!Directory.Exists(parent))
                return null;

            string exact = Path.Combine(parent, name);
            if (File.Exists(exact))
                return exact;

            try
            {
                return Directory.EnumerateFiles(parent, "*", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(path => string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }
    }
}
