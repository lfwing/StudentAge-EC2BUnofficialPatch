using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using EC2BUnofficialPatch.Core;
using EC2BUnofficialPatch.Workshop;
using HarmonyLib;

namespace EC2BUnofficialPatch.Features.Mechanics
{
    /// <summary>
    /// 为原版 ModCtrl.LoadModCfgs 构建“同 Mod 运行时 CFG 视图”。
    ///
    /// 目标：
    /// - 有原版兼容 CFG + 插件增强 CFG：增强版替代同名文件；
    /// - 没有原版同名 CFG、甚至没有 Cfgs/zh-cn 目录：增强版仍会作为该 Mod 的 CFG 被原版加载；
    /// - 不改写 Workshop 原文件；
    /// - 仍由原版 LoadModCfgs -> cfgMaps -> MergeCfgsAsync 完成反序列化和合并，保持 Mod 加载顺序及 ID 冲突规则。
    /// </summary>
    internal sealed class ModCfgOverrideModule : IPluginModule
    {
        private static readonly IReadOnlyList<ModuleLogItem> Items = Array.Empty<ModuleLogItem>();

        public string Key => "mechanics.mod-cfg-override";
        public IReadOnlyList<ModuleLogItem> LogItems => Items;

        public void Load(Harmony harmony, PluginServices services)
        {
            ModCfgOverridePatches.Initialize(services?.ContentRoots);
            MethodInfo target = AccessTools.Method(
                typeof(ModCtrl),
                "LoadModCfgs",
                new[] { typeof(ulong), typeof(string) })
                ?? throw new MissingMethodException(typeof(ModCtrl).FullName, "LoadModCfgs(ulong,string)");

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(
                    typeof(ModCfgOverridePatches),
                    nameof(ModCfgOverridePatches.LoadModCfgsPrefix)));
        }
    }

    internal static class ModCfgOverridePatches
    {
        private const string OverlayDirectoryName = "CfgOverlay";
        private static readonly object SyncRoot = new object();
        private static readonly HashSet<string> WorkshopRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> LoggedMappings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static string _overlayRoot;

        internal static void Initialize(ContentRootCatalog catalog)
        {
            lock (SyncRoot)
            {
                WorkshopRoots.Clear();
                foreach (ContentRoot root in catalog?.Roots ?? Array.Empty<ContentRoot>())
                    WorkshopRoots.Add(NormalizeRoot(root.Path));
                LoggedMappings.Clear();

                _overlayRoot = GetDefaultOverlayRoot();

                // 运行时视图每次启动重新生成，避免 Workshop 更新后读取旧缓存。
                try
                {
                    if (Directory.Exists(_overlayRoot))
                        Directory.Delete(_overlayRoot, true);
                    Directory.CreateDirectory(_overlayRoot);
                }
                catch (Exception exception)
                {
                    PatchLog.Warning(
                        $"机制模块-CFG 运行时覆盖目录初始化失败，将在首次需要时重试：path={_overlayRoot}, reason={ModuleHost.GetReason(exception)}");
                }
            }
        }

        /// <summary>
        /// Harmony 参数按序号绑定，避免私有方法参数名在不同反编译/构建环境中变化。
        /// __0 = modId, __1 = 原版 Cfgs/zh-cn 路径。
        /// </summary>
        public static void LoadModCfgsPrefix(ulong __0, ref string __1)
        {
            try
            {
                if (!TryBuildOverlay(__0, __1, out string overlayPath, out List<CfgOverlayDecision> decisions))
                    return;

                string originalPath = __1;
                __1 = overlayPath;

                foreach (CfgOverlayDecision decision in decisions)
                {
                    string logKey = __0 + "|" + decision.CanonicalFileName + "|" + decision.SourcePath;
                    lock (SyncRoot)
                    {
                        if (!LoggedMappings.Add(logKey))
                            continue;
                    }

                    PatchLog.Info(
                        $"机制模块-{decision.FeatureName}增强 CFG 已纳入当前 Mod 的运行时配置视图：" +
                        $"mod={__0}, mode={(decision.ReplacedOriginal ? "替代原版同名CFG" : "原版同名CFG不存在，主动注入")}, " +
                        $"source={decision.SourcePath}, runtime={Path.Combine(overlayPath, decision.CanonicalFileName)}");
                }

                PatchLog.Info(
                    $"机制模块-Mod CFG 运行时视图已启用：mod={__0}, original={originalPath}, overlay={overlayPath}");
            }
            catch (Exception exception)
            {
                PatchLog.Warning(
                    $"机制模块-CFG 运行时视图构建失败，继续使用原版路径：mod={__0}, path={__1}, reason={ModuleHost.GetReason(exception)}");
            }
        }

        internal static bool TryBuildOverlay(
            ulong modId,
            string originalCfgPath,
            out string overlayPath,
            out List<CfgOverlayDecision> decisions)
        {
            overlayPath = null;
            decisions = new List<CfgOverlayDecision>();

            if (!TryGetWorkshopModRoot(originalCfgPath, out string modRoot))
                return false;

            string pluginDirectory = ExternalResourceResolver.FindChildDirectory(modRoot, "EC2BUnofficialPatch");
            if (pluginDirectory == null)
                return false;

            CfgOverlayDecision loveDraw = TryCreateDecision(
                originalCfgPath,
                pluginDirectory,
                PluginConfig.LoveDrawExternalResources != null && PluginConfig.LoveDrawExternalResources.Value,
                "情侣画修复",
                "LoveDraw",
                "LoveDrawCfg.json");
            if (loveDraw != null)
                decisions.Add(loveDraw);

            CfgOverlayDecision minigame = TryCreateDecision(
                originalCfgPath,
                pluginDirectory,
                PluginConfig.MinigameMechanics != null && PluginConfig.MinigameMechanics.Value,
                "社交小游戏修复",
                "Minigame",
                "MinigameActionCfg.json");
            if (minigame != null)
                decisions.Add(minigame);

            if (decisions.Count == 0)
                return false;

            string root = _overlayRoot;
            if (string.IsNullOrWhiteSpace(root))
            {
                root = GetDefaultOverlayRoot();
            }

            overlayPath = Path.Combine(root, modId.ToString(), "zh-cn");
            RecreateDirectory(overlayPath);

            HashSet<string> overriddenNames = new HashSet<string>(
                decisions.Select(item => item.CanonicalFileName),
                StringComparer.OrdinalIgnoreCase);

            // 原版只枚举当前目录顶层的 *Cfg.json，因此运行时视图保持完全相同的平面结构。
            if (Directory.Exists(originalCfgPath))
            {
                foreach (string source in Directory.GetFiles(originalCfgPath, "*Cfg.json", SearchOption.TopDirectoryOnly))
                {
                    string fileName = Path.GetFileName(source);
                    if (overriddenNames.Contains(fileName))
                        continue;
                    File.Copy(source, Path.Combine(overlayPath, fileName), true);
                }
            }

            foreach (CfgOverlayDecision decision in decisions)
            {
                File.Copy(
                    decision.SourcePath,
                    Path.Combine(overlayPath, decision.CanonicalFileName),
                    true);
            }

            return true;
        }

        private static CfgOverlayDecision TryCreateDecision(
            string originalCfgPath,
            string pluginDirectory,
            bool featureEnabled,
            string featureName,
            string featureDirectoryName,
            string canonicalFileName)
        {
            if (!featureEnabled)
                return null;

            string featureDirectory = ExternalResourceResolver.FindChildDirectory(pluginDirectory, featureDirectoryName);
            if (featureDirectory == null)
                return null;

            string enhancedFile = ExternalResourceResolver.FindChildFile(featureDirectory, canonicalFileName);
            if (enhancedFile == null)
                return null;

            bool originalExists = Directory.Exists(originalCfgPath) &&
                                  Directory.EnumerateFiles(originalCfgPath, "*", SearchOption.TopDirectoryOnly)
                                      .Any(file => string.Equals(
                                          Path.GetFileName(file),
                                          canonicalFileName,
                                          StringComparison.OrdinalIgnoreCase));

            return new CfgOverlayDecision(
                featureName,
                canonicalFileName,
                enhancedFile,
                originalExists);
        }

        private static bool TryGetWorkshopModRoot(string originalCfgPath, out string modRoot)
        {
            modRoot = null;
            if (string.IsNullOrWhiteSpace(originalCfgPath) || !Path.IsPathRooted(originalCfgPath))
                return false;

            string fullPath = Path.GetFullPath(originalCfgPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            DirectoryInfo languageDirectory = new DirectoryInfo(fullPath);
            DirectoryInfo cfgDirectory = languageDirectory.Parent;
            DirectoryInfo candidateRoot = cfgDirectory?.Parent;
            if (cfgDirectory == null || candidateRoot == null)
                return false;
            if (!string.Equals(cfgDirectory.Name, "Cfgs", StringComparison.OrdinalIgnoreCase))
                return false;

            string normalized = NormalizeRoot(candidateRoot.FullName);
            if (!WorkshopRoots.Contains(normalized))
                return false;

            modRoot = candidateRoot.FullName;
            return true;
        }

        private static void RecreateDirectory(string directory)
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
            Directory.CreateDirectory(directory);
        }


        private static string GetDefaultOverlayRoot()
        {
            DirectoryInfo bepinExRoot = Directory.GetParent(Paths.PluginPath);
            string root = bepinExRoot?.FullName ?? Paths.PluginPath;
            return Path.Combine(root, "cache", "EC2BUnofficialPatch", OverlayDirectoryName);
        }

        private static string NormalizeRoot(string path)
        {
            return Path.GetFullPath(path ?? string.Empty).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        }
    }

    internal sealed class CfgOverlayDecision
    {
        internal CfgOverlayDecision(
            string featureName,
            string canonicalFileName,
            string sourcePath,
            bool replacedOriginal)
        {
            FeatureName = featureName;
            CanonicalFileName = canonicalFileName;
            SourcePath = sourcePath;
            ReplacedOriginal = replacedOriginal;
        }

        internal string FeatureName { get; }
        internal string CanonicalFileName { get; }
        internal string SourcePath { get; }
        internal bool ReplacedOriginal { get; }
    }
}
