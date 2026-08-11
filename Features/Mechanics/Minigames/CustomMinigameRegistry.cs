using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using EC2BUnofficialPatch.Core;
using EC2BUnofficialPatch.Workshop;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sdk;

namespace EC2BUnofficialPatch.Features.Mechanics.Minigames
{
    internal enum CustomMinigameKind
    {
        Direct,
        Alias,
        Dialogue,
        External
    }

    internal sealed class CustomMinigameDefinition
    {
        internal int Id;
        internal CustomMinigameKind Kind;
        internal int TargetId;
        internal string DllPath;
        internal string TypeName;
        internal Type ExternalType;
        internal string SourceFile;
        internal IReadOnlyDictionary<string, string> Parameters;
    }

    internal sealed class ResolvedMinigameImplementation
    {
        internal int ImplementationId;
        internal CustomMinigameKind Kind;
        internal OriginalMinigameDescriptor Original;
        internal CustomMinigameDefinition Definition;

        internal bool IsCustom => Kind == CustomMinigameKind.Dialogue || Kind == CustomMinigameKind.External;
        internal bool CanOpenEmbedded =>
            Kind == CustomMinigameKind.External ||
            (Original != null && Original.CanOpenEmbedded);
        internal bool CanOpenAsFallback =>
            Kind == CustomMinigameKind.Dialogue ||
            Kind == CustomMinigameKind.External ||
            (Original != null && Original.CanOpenAsFallback);
    }

    internal sealed class CustomMinigameRegistry
    {
        private const string FileName = "CustomMinigamecfg.json";
        private readonly Dictionary<int, CustomMinigameDefinition> _definitions =
            new Dictionary<int, CustomMinigameDefinition>();

        private CustomMinigameRegistry() { }

        internal int ExplicitCount => _definitions.Count;
        internal bool HasExplicitMapping(int logicalId) => _definitions.ContainsKey(logicalId);

        internal static CustomMinigameRegistry Load(ContentRootCatalog roots)
        {
            CustomMinigameRegistry registry = new CustomMinigameRegistry();
            foreach (string file in DiscoverFiles(roots))
            {
                registry.LoadFile(file);
            }

            PatchLog.Registration($"机制模块-自定义小游戏注册完成：count={registry.ExplicitCount}");
            return registry;
        }

        internal bool TryResolve(int requestedId, out ResolvedMinigameImplementation implementation)
        {
            implementation = null;
            if (!_definitions.TryGetValue(requestedId, out CustomMinigameDefinition definition))
            {
                if (!OriginalMinigameCatalog.TryGet(requestedId, out OriginalMinigameDescriptor original))
                {
                    return false;
                }

                implementation = new ResolvedMinigameImplementation
                {
                    ImplementationId = requestedId,
                    Kind = CustomMinigameKind.Direct,
                    Original = original
                };
                return true;
            }

            switch (definition.Kind)
            {
                case CustomMinigameKind.Direct:
                    if (!OriginalMinigameCatalog.TryGet(definition.Id, out OriginalMinigameDescriptor direct))
                    {
                        return false;
                    }
                    implementation = new ResolvedMinigameImplementation
                    {
                            ImplementationId = direct.Id,
                        Kind = definition.Kind,
                        Original = direct,
                        Definition = definition
                    };
                    return true;

                case CustomMinigameKind.Alias:
                    if (!OriginalMinigameCatalog.TryGet(definition.TargetId, out OriginalMinigameDescriptor alias))
                    {
                        return false;
                    }
                    implementation = new ResolvedMinigameImplementation
                    {
                            ImplementationId = alias.Id,
                        Kind = definition.Kind,
                        Original = alias,
                        Definition = definition
                    };
                    return true;

                case CustomMinigameKind.Dialogue:
                case CustomMinigameKind.External:
                    implementation = new ResolvedMinigameImplementation
                    {
                            ImplementationId = requestedId,
                        Kind = definition.Kind,
                        Definition = definition
                    };
                    return true;

                default:
                    return false;
            }
        }

        internal bool OpenRegisteredImplementation(
            ResolvedMinigameImplementation implementation,
            MiniGameStageSession session,
            IReadOnlyList<double> launchParameters,
            MiniGameFromType launchFrom,
            int launchSourceId,
            Action success,
            Action fail,
            Action<float> result)
        {
            if (implementation == null || implementation.Definition == null || session == null)
            {
                return false;
            }

            CustomMinigameDefinition definition = implementation.Definition;
            long token = session.Token;
            int logicalId = session.LogicalGameId;
            int npcId = session.NpcId;
            int cfgId = session.CfgId;

            Action<bool, int> complete = (isWin, selectId) =>
            {
                if (!MiniGameStageCoordinator.CompleteFromAdapter(
                        token,
                        isWin,
                        selectId,
                        $"registered-{definition.Kind.ToString().ToLowerInvariant()}"))
                {
                    return;
                }

                MiniGameStageSession active = MiniGameStageCoordinator.Current;
                bool embedded = active != null && active.Token == token && active.EmbeddedLaunch;
                if (embedded)
                {
                    if (isWin)
                    {
                        success?.Invoke();
                    }
                    else if (fail != null)
                    {
                        fail();
                    }
                    else if (active.LaunchFrom == MiniGameFromType.Talk)
                    {
                        // 原版 Talk 内嵌只提供 success 槽作为流程完成 callback。
                        success?.Invoke();
                    }

                    if (success == null && fail == null)
                    {
                        result?.Invoke(selectId);
                    }
                    return;
                }

                active = MiniGameStageCoordinator.Current;
                if (active != null && active.Token == token && active.Settled)
                {
                    MiniGameStageCoordinator.Clear("registered-level-finished");
                }
            };

            if (definition.Kind == CustomMinigameKind.Dialogue)
            {
                if (session.EmbeddedLaunch)
                {
                    PatchLog.Error(
                        "机制模块-纯对话实现不能内嵌到 Talk/Option；" +
                        $"id={logicalId}, npc={npcId}, cfg={cfgId}");
                    return false;
                }

                PatchLog.Info(
                    $"机制模块-纯对话小游戏直接成功结算：id={logicalId}, npc={npcId}, cfg={cfgId}");
                complete(true, 0);
                return true;
            }

            if (definition.Kind != CustomMinigameKind.External || definition.ExternalType == null)
            {
                return false;
            }

            try
            {
                ICustomMinigame instance =
                    (ICustomMinigame)Activator.CreateInstance(definition.ExternalType);
                CustomMinigameContext context = new CustomMinigameContext(
                    logicalId,
                    npcId,
                    cfgId,
                    definition.SourceFile,
                    definition.Parameters,
                    launchParameters,
                    launchFrom,
                    launchSourceId,
                    complete);
                instance.Open(context);
                PatchLog.Info(
                    "机制模块-已调用外部小游戏：" +
                    $"id={logicalId}, npc={npcId}, cfg={cfgId}, type={definition.TypeName}");
                return true;
            }
            catch (Exception exception)
            {
                PatchLog.Exception(
                    $"机制模块-外部小游戏 Open 异常：id={logicalId}, type={definition.TypeName}",
                    exception);
                return false;
            }
        }

        private static IEnumerable<string> DiscoverFiles(ContentRootCatalog roots)
        {
            if (roots?.Roots == null) yield break;
            HashSet<string> yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ContentRoot root in roots.Roots)
            {
                if (root == null || string.IsNullOrWhiteSpace(root.Path) || !Directory.Exists(root.Path))
                {
                    continue;
                }

                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(root.Path, FileName, SearchOption.AllDirectories)
                        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                }
                catch (Exception exception)
                {
                    PatchLog.Warning(
                        $"机制模块-扫描 {FileName} 失败：root={root.Path}, reason={exception.Message}");
                    continue;
                }

                foreach (string file in files)
                {
                    string full = Path.GetFullPath(file);
                    if (yielded.Add(full)) yield return full;
                }
            }
        }

        private void LoadFile(string file)
        {
            try
            {
                JToken root = JToken.Parse(File.ReadAllText(file));
                JArray entries = root as JArray ?? (root as JObject)?["minigames"] as JArray;
                if (entries == null)
                {
                    PatchLog.Warning($"机制模块-{FileName} 缺少 minigames 数组：file={file}");
                    return;
                }

                foreach (JToken token in entries)
                {
                    ParseEntry(token as JObject, file);
                }
            }
            catch (Exception exception)
            {
                PatchLog.Exception($"机制模块-读取 {FileName} 失败：file={file}", exception);
            }
        }

        private void ParseEntry(JObject entry, string source)
        {
            if (entry == null) return;
            int? id = ReadInt(entry, "id", "gameId");
            string type = (entry["type"] ?? entry["kind"])?.ToString()?.Trim().ToLowerInvariant();
            if (!id.HasValue || id.Value <= 0 || string.IsNullOrWhiteSpace(type))
            {
                PatchLog.Warning($"机制模块-忽略无效小游戏注册：file={source}, value={entry}");
                return;
            }

            CustomMinigameDefinition definition = new CustomMinigameDefinition
            {
                Id = id.Value,
                SourceFile = source,
                Parameters = ReadParameters(entry["parameters"] ?? entry["params"])
            };

            switch (type)
            {
                case "direct":
                case "original":
                    definition.Kind = CustomMinigameKind.Direct;
                    if (!OriginalMinigameCatalog.HasDispatcher(definition.Id))
                    {
                        WarnInvalid(source, definition.Id, "direct 必须使用原版 OpenMiniGame 已存在的 ID");
                        return;
                    }
                    break;

                case "alias":
                    definition.Kind = CustomMinigameKind.Alias;
                    definition.TargetId = ReadInt(entry, "targetId", "target", "originalId") ?? 0;
                    if (!OriginalMinigameCatalog.HasDispatcher(definition.TargetId))
                    {
                        WarnInvalid(
                            source,
                            definition.Id,
                            $"alias.targetId={definition.TargetId} 不是原版玩法 ID");
                        return;
                    }
                    break;

                case "dialogue":
                case "dialogueonly":
                case "text":
                    definition.Kind = CustomMinigameKind.Dialogue;
                    break;

                case "external":
                case "dll":
                    definition.Kind = CustomMinigameKind.External;
                    definition.DllPath = ResolveRelativePath(source, entry["dll"]?.ToString());
                    definition.TypeName = entry["class"]?.ToString() ?? entry["typeName"]?.ToString();
                    if (!TryLoadExternal(definition)) return;
                    break;

                default:
                    WarnInvalid(source, definition.Id, $"未知 type={type}");
                    return;
            }

            if (_definitions.TryGetValue(definition.Id, out CustomMinigameDefinition old))
            {
                PatchLog.Warning(
                    "机制模块-重复小游戏注册，后加载文件覆盖前者：" +
                    $"id={definition.Id}, old={old.SourceFile}, new={source}");
            }

            _definitions[definition.Id] = definition;
            PatchLog.Registration(
                "机制模块-注册自定义小游戏：" +
                $"id={definition.Id}, type={definition.Kind}, target={definition.TargetId}, file={source}");
        }

        private static bool TryLoadExternal(CustomMinigameDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(definition.DllPath) ||
                !File.Exists(definition.DllPath) ||
                string.IsNullOrWhiteSpace(definition.TypeName))
            {
                WarnInvalid(definition.SourceFile, definition.Id, "external 必须提供有效的 dll 和 class");
                return false;
            }

            try
            {
                Assembly assembly = Assembly.LoadFrom(definition.DllPath);
                Type type = assembly.GetType(definition.TypeName, false, false);
                if (type == null ||
                    !typeof(ICustomMinigame).IsAssignableFrom(type) ||
                    type.IsAbstract ||
                    type.GetConstructor(Type.EmptyTypes) == null)
                {
                    WarnInvalid(
                        definition.SourceFile,
                        definition.Id,
                        $"class={definition.TypeName} 必须实现 ICustomMinigame 并提供无参构造函数");
                    return false;
                }

                definition.ExternalType = type;
                return true;
            }
            catch (Exception exception)
            {
                PatchLog.Exception(
                    $"机制模块-加载外部小游戏 DLL 失败：id={definition.Id}, dll={definition.DllPath}",
                    exception);
                return false;
            }
        }

        private static string ResolveRelativePath(string jsonFile, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return Path.GetFullPath(
                Path.IsPathRooted(value)
                    ? value
                    : Path.Combine(Path.GetDirectoryName(jsonFile) ?? string.Empty, value));
        }

        private static IReadOnlyDictionary<string, string> ReadParameters(JToken token)
        {
            Dictionary<string, string> result =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!(token is JObject obj)) return result;

            foreach (JProperty property in obj.Properties())
            {
                result[property.Name] = property.Value.Type == JTokenType.String
                    ? property.Value.Value<string>()
                    : property.Value.ToString(Formatting.None);
            }
            return result;
        }

        private static int? ReadInt(JObject obj, params string[] names)
        {
            foreach (string name in names)
            {
                JToken token = obj[name];
                if (token == null) continue;
                if (token.Type == JTokenType.Integer) return token.Value<int>();
                if (int.TryParse(
                        token.ToString(),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int value))
                {
                    return value;
                }
            }
            return null;
        }

        private static void WarnInvalid(string source, int id, string reason) =>
            PatchLog.Warning(
                $"机制模块-忽略无效小游戏注册：id={id}, reason={reason}, file={source}");
    }
}
