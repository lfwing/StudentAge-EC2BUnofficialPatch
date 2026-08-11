using System.IO;
using System.Text;
using BepInEx;

namespace EC2BUnofficialPatch.Core
{
    internal static class TemplateMaintenance
    {
        private const string ScreenPaperReadme =
            "5001屏幕纸条扩展模板\r\n\r\n" +
            "纸条文字、署名、缩放和对齐仍只在原版 PaperCfg.json 注册；插件不修改原版 CFG 结构。\r\n" +
            "编辑 Custompaper.json，只声明某个原版纸条 id 应使用的图片。图片放在本目录，支持 PNG、JPG、JPEG。\r\n" +
            "image 必须是相对于本目录的路径，不允许绝对路径或使用 .. 跳出目录。\r\n" +
            "未声明、image 留空、文件缺失或图片损坏时会自动使用原版图片。\r\n" +
            "id 必须已由当前原版/Mod PaperCfg 注册；重复 id 会报错，并让冲突项全部回退原版。\r\n\r\n" +
            "实际使用时，请把 ScreenPaper 文件夹放到 Steam Workshop Mod 根目录，\r\n" +
            "或放到 Mod 根目录/EC2BUnofficialPatch 下。本模板目录不会被插件读取。\r\n";

        private const string RoleAvailabilityReadme =
            "角色可用性外置配置\r\n\r\n" +
            "RoleAvailabilityCfg.json 只控制已登记角色何时进入社交系统，以及是否参加考试；不改变角色是否存在。\r\n" +
            "cond 完全使用原版 Condition 数组，并由 CommonEvtMgr.IsMatchCondition 执行。\r\n" +
            "takeExam=false 时不要求提供考试数据；takeExam=true 时必须具有完整 Person、PersonGrow、ExamRank 与 Classmate 数据。\r\n" +
            "重复 personId、非法 Condition、缺失字段或不完整考试数据都会记录日志并安全排除。\r\n\r\n" +
            "Workshop Mod 中请放到：Mod根目录/EC2BUnofficialPatch/RoleAvailabilityCfg.json。\r\n";

        private const string RoleAvailabilityJson =
            "{\r\n" +
            "  \"roles\": []\r\n" +
            "}\r\n";

        private const string ScreenLyrcisReadme =
            "5002屏幕滚动歌词扩展模板\r\n\r\n" +
            "编辑 CustomScreenLyrcis.json，在 lyrics 数组中添加歌词。\r\n" +
            "id 为歌词 ID；text 支持使用 \\n 换行；其余字段控制字号、行距、颜色和对齐。\r\n" +
            "audio 是可选的原版/BetterAudio 音乐 ID；省略时按文字长度和滚动距离动态决定演出时长。\r\n" +
            "填写 audio 时会暂停当前音乐并播放该 ID，结束或长按左键跳过后恢复先前音乐。\r\n\r\n" +
            "实际使用时，请把 ScreenLyrcis 文件夹放到 Steam Workshop Mod 根目录，\r\n" +
            "或放到 Mod 根目录/EC2BUnofficialPatch 下。本模板目录不会被插件读取。\r\n";

        private const string ScreenPaperJson =
            "{\r\n" +
            "  \"papers\": [\r\n" +
            "    { \"id\": 1, \"image\": \"paper_1.png\" }\r\n" +
            "  ]\r\n" +
            "}\r\n";

        private const string ComicReadme =
            "4016 漫画显示扩展模板\r\n\r\n" +
            "本目录旁会创建一个空的 comic 文件夹。实际 Mod 中，comic 文件夹可以放在 Mod 内任意位置，\r\n" +
            "例如 EC2BUnofficialPatch/comic、Textures/comic；插件会递归嗅探文件夹名恰好为 comic 的目录。\r\n" +
            "插件只读取 comic 文件夹内部内容。comic 下必须再建立漫画子文件夹。\r\n" +
            "图片文件名必须严格为 {图号}-{分镜号}.png/.jpg/.jpeg，例如 1-1.png。\r\n" +
            "每个图号实际存在的连续分镜数必须与 CGCfg.json 的 comic 数组完全一致。\r\n" +
            "建议 urls 使用 Mods/<packageId>/.../comic/<漫画子文件夹>；packageId 是 ModMetadata.packageId，不是 Workshop 数字 ID。\r\n" +
            "Mods/<packageId>/ 是游戏逻辑前缀，不要求 Workshop 物理目录中真的存在 Mods/<packageId>。新 JSON 推荐使用 /；JSON 中旧式双反斜杠 \\\\ 写法仍兼容。\r\n" +
            "move 仍按原版规则使用：缺少某页 move 只表示该页不做位移动画，并不要求与 comic 等长。\r\n" +
            "详细说明见工程 Docs/ComicExternalResources.md。\r\n";

        private const string LoveDrawReadme =
            "情侣画 LoveDraw 外置资源通道\r\n\r\n" +
            "LoveDrawCfg 仍按原版方式放在 Mod 的 Cfgs/zh-cn/LoveDrawCfg.json 中，\r\n" +
            "不需要增加任何自定义字段。img、video 填写本目录内的相对路径。\r\n\r\n" +
            "示例目录：\r\n" +
            "LoveDraw/paint/alice_01.png\r\n" +
            "LoveDraw/paint/alice_01.mp4\r\n\r\n" +
            "对应 CFG：\r\n" +
            "\"img\": \"paint/alice_01.png\",\r\n" +
            "\"video\": \"paint/alice_01.mp4\"\r\n\r\n" +
            "也可以省略扩展名，例如 paint/alice_01；插件会自动匹配支持的格式。\r\n" +
            "图片支持 PNG、JPG、JPEG；视频支持 MP4、WEBM、MOV、M4V、OGV。\r\n" +
            "目录名 LoveDraw/Lovedraw 大小写均可。禁止使用 .. 跳出本目录。\r\n\r\n" +
            "资源目录支持：\r\n" +
            "1. Steam Workshop Mod 根目录/LoveDraw\r\n" +
            "2. Mod 根目录/EC2BUnofficialPatch/LoveDraw\r\n" +
            "3. BepInEx/plugins/EC2BUnofficialPatch/LoveDraw（本目录）\r\n\r\n" +
            "没有命中外置文件时会保留原版 Addressables 加载方式，因此官方 cfg 无需修改。\r\n\r\n" +
            "可选 CFG 双版本：保留 Mod/Cfgs/zh-cn/LoveDrawCfg.json 作为无插件兼容版；\r\n" +
            "若同一 Mod 的 EC2BUnofficialPatch/LoveDraw/LoveDrawCfg.json 存在，插件会仅在运行时改读该文件。\r\n" +
            "不会覆盖磁盘上的兼容版文件，也绝不会从其他 Workshop Mod 借用 CFG。\r\n";

        private const string MinigameReadme =
            "自定义小游戏自动注册模板\r\n\r\n" +
            "原版阶段数据仍使用 PersonGrowCfg、MinigameCfg、MinigameActionCfg。\r\n" +
            "CustomMinigamecfg.json 只声明后备实现，不向原版 cfg 增加字段。\r\n\r\n" +
            "社交阶段会优先使用 startTalk 的 Talk/Option miniGame 内嵌玩法。\r\n" +
            "内嵌玩法会保留原版参数，并把结算回写到当前 NPC 的逻辑小游戏阶段。\r\n" +
            "只有本阶段没有实际打开内嵌玩法时，插件才尝试 CustomMinigamecfg 后备实现。\r\n\r\n" +
            "type 支持四种值：\r\n" +
            "- direct：原版直连，id 必须是原版 OpenMiniGame 已存在的 ID。\r\n" +
            "- alias：玩法别名，targetId 指向原版玩法，例如 110 -> 5。\r\n" +
            "- dialogue：纯对话，仅可作为后备；startTalk 完成后自动按成功结算。\r\n" +
            "- external：外部 DLL；dll 为相对本 JSON 的路径，class 必须实现 ICustomMinigame。\r\n\r\n" +
            "参数型玩法（如 6 话术、21 战斗）必须在 Talk/Option 中内嵌打开。\r\n" +
            "特殊/情侣玩法用于社交阶段时，若没有更明确结果，正常关闭将按成功推进。\r\n" +
            "插件会递归扫描 workshop/content/1991040 的所有 Mod 目录内同名文件。\r\n" +
            "外部 DLL 每次打开都会创建新实例，并可读取 context.LaunchParameters。\r\n" +
            "结束时调用 context.Complete(win, selectId)。\r\n\r\n" +
            "可选 CFG 双版本：保留 Mod/Cfgs/zh-cn/MinigameActionCfg.json 作为无插件兼容版；\r\n" +
            "若同一 Mod 的 EC2BUnofficialPatch/Minigame/MinigameActionCfg.json 存在，插件会仅在运行时改读该文件。\r\n" +
            "不会覆盖磁盘上的兼容版文件，也不会跨 Mod 查找替代 CFG。\r\n";

        private const string ScreenLyrcisJson =
            "{\r\n" +
            "  \"lyrics\": [\r\n" +
            "    {\r\n" +
            "      \"id\": 1001,\r\n" +
            "      \"audio\": 2,\r\n" +
            "      \"text\": \"第一行\\n第二行\",\r\n" +
            "      \"fontSize\": 50,\r\n" +
            "      \"lineSpacing\": 50,\r\n" +
            "      \"fontColor\": \"#FFFFFFFF\",\r\n" +
            "      \"alignH\": 2,\r\n" +
            "      \"alignV\": 256\r\n" +
            "    }\r\n" +
            "  ]\r\n" +
            "}\r\n";

        private const string MinigameJson =
            "{\r\n" +
            "  \"minigames\": [\r\n" +
            "    { \"id\": 14, \"type\": \"direct\" },\r\n" +
            "    { \"id\": 110, \"type\": \"alias\", \"targetId\": 5 },\r\n" +
            "    { \"id\": 54, \"type\": \"dialogue\" },\r\n" +
            "    {\r\n" +
            "      \"id\": 200,\r\n" +
            "      \"type\": \"external\",\r\n" +
            "      \"dll\": \"TankBattleMinigame.dll\",\r\n" +
            "      \"class\": \"Example.TankBattleMinigame\",\r\n" +
            "      \"parameters\": { \"difficulty\": 2, \"timeLimit\": 60 }\r\n" +
            "    }\r\n" +
            "  ]\r\n" +
            "}\r\n";

        internal static void Ensure()
        {
            string templateRoot = Path.Combine(Paths.PluginPath, "EC2BUnofficialPatch");
            WriteRoleAvailabilityTemplate(templateRoot);
            TryWriteComicTemplate(templateRoot);
            WriteTemplate(
                Path.Combine(templateRoot, "ScreenPaper"),
                ScreenPaperReadme,
                "Custompaper.json",
                ScreenPaperJson);
            WriteTemplate(
                Path.Combine(templateRoot, "ScreenLyrcis"),
                ScreenLyrcisReadme,
                "CustomScreenLyrcis.json",
                ScreenLyrcisJson);
            WriteTemplate(
                Path.Combine(templateRoot, "LoveDraw"),
                LoveDrawReadme,
                null,
                null);
            WriteTemplate(
                Path.Combine(templateRoot, "Minigame"),
                MinigameReadme,
                "CustomMinigamecfg.json",
                MinigameJson);
        }

        private static void WriteRoleAvailabilityTemplate(string templateRoot)
        {
            try
            {
                Directory.CreateDirectory(templateRoot);
                WriteTextFile(Path.Combine(templateRoot, "RoleAvailability说明.txt"), RoleAvailabilityReadme);
                string jsonPath = Path.Combine(templateRoot, "RoleAvailabilityCfg.json");
                if (!File.Exists(jsonPath))
                    WriteTextFile(jsonPath, RoleAvailabilityJson);
            }
            catch (System.Exception exception)
            {
                PatchLog.Warning(
                    $"底层服务模块-RoleAvailability 模板写入失败，忽略模板但继续启动：" +
                    $"path={templateRoot}, reason={ModuleHost.GetReason(exception)}");
            }
        }

        private static void TryWriteComicTemplate(string templateRoot)
        {
            try
            {
                Directory.CreateDirectory(Path.Combine(templateRoot, "comic"));
                WriteTextFile(Path.Combine(templateRoot, "comic说明.txt"), ComicReadme);
            }
            catch (System.Exception exception)
            {
                PatchLog.Warning(
                    $"底层服务模块-4016漫画模板写入失败，忽略模板但继续启动：" +
                    $"path={templateRoot}, reason={ModuleHost.GetReason(exception)}");
            }
        }

        private static void WriteTemplate(
            string directory,
            string readme,
            string jsonFileName,
            string json)
        {
            try
            {
                Directory.CreateDirectory(directory);
                UTF8Encoding encoding = new UTF8Encoding(false);
                string readmePath = Path.Combine(directory, "readme.txt");
                File.WriteAllText(readmePath, readme, encoding);

                if (!string.IsNullOrEmpty(jsonFileName) && json != null)
                {
                    string jsonPath = Path.Combine(directory, jsonFileName);
                    if (!File.Exists(jsonPath))
                    {
                        File.WriteAllText(jsonPath, json, encoding);
                    }
                }
            }
            catch (System.Exception exception)
            {
                PatchLog.Warning(
                    $"底层服务模块-模板写入失败，忽略模板但继续启动：" +
                    $"path={directory}, reason={ModuleHost.GetReason(exception)}");
            }
        }

        private static void WriteTextFile(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }
    }
}
