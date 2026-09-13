using System;
using System.IO;
using System.Text;

namespace LFBetterAudio.Templates
{
    /// <summary>
    /// 在 BepInEx/plugins/BetterAudio 下维护一份“仅供复制”的 Mod 作者模板。
    /// 该目录绝不参与 BetterAudio 资源注册，也不是插件 DLL 的强制安装目录。
    /// </summary>
    internal static class ModAuthorTemplateInstaller
    {
        internal const string TemplateFolderName = "BetterAudio";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        internal static void EnsureInstalled(string templateDirectory)
        {
            if (string.IsNullOrWhiteSpace(templateDirectory))
            {
                throw new ArgumentException("templateDirectory 不能为空。", nameof(templateDirectory));
            }

            string root = Path.GetFullPath(templateDirectory);
            string audioDirectory = Path.Combine(root, "Audio");
            string timelineDirectory = Path.Combine(root, "Timeline");

            Directory.CreateDirectory(root);
            Directory.CreateDirectory(audioDirectory);
            Directory.CreateDirectory(timelineDirectory);

            WriteIfMissing(
                Path.Combine(root, "BetterAudio.json"),
                CreateTemplateJson());

            WriteIfMissing(
                Path.Combine(audioDirectory, "README.txt"),
                "把 Mod 自定义音频文件放在这里。\n" +
                "BetterAudio.json 中建议使用相对路径，例如：\n" +
                "  \"audioPath\": \"Audio/MyAudio.mp3\"\n\n" +
                "支持格式取决于游戏当前 ResMgr.LoadAudioAsync / Unity 音频解码能力。\n");

            WriteIfMissing(
                Path.Combine(timelineDirectory, "example.lrc"),
                "[00:00.00]第一条示例时间轴文本\n" +
                "[00:03.50]第二条示例时间轴文本\n" +
                "[00:07.0]第三条示例时间轴文本\n");

            WriteIfMissing(
                Path.Combine(timelineDirectory, "original_12345.lrc"),
                "[00:00.00]把此文件替换为原版音频对应的 Timeline LRC\n");
        }

        private static void WriteIfMissing(string path, string content)
        {
            if (File.Exists(path))
            {
                return;
            }

            File.WriteAllText(path, content ?? string.Empty, Utf8NoBom);
        }

        private static string CreateTemplateJson()
        {
            return
                "{\n" +
                "  \"audios\": [\n" +
                "    {\n" +
                "      \"id\": 90001,\n" +
                "      \"name\": \"自定义音乐示例\",\n" +
                "      \"audioPath\": \"Audio/example.mp3\",\n" +
                "      \"timelinePath\": \"Timeline/example.lrc\",\n" +
                "      \"volume\": 0.85,\n" +
                "      \"type\": 1\n" +
                "    },\n" +
                "    {\n" +
                "      \"id\": 12345,\n" +
                "      \"name\": \"原版音频外挂 Timeline 示例（请改成真实原版音频 ID）\",\n" +
                "      \"audioPath\": \"\",\n" +
                "      \"timelinePath\": \"Timeline/original_12345.lrc\",\n" +
                "      \"volume\": 1.0,\n" +
                "      \"type\": 1\n" +
                "    }\n" +
                "  ]\n" +
                "}\n";
        }
    }
}
