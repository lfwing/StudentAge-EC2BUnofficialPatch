using System;
using System.IO;
using System.Text;
using BepInEx;
using EC2BUnofficialPatch.Core;

namespace EC2BUnofficialPatch.Features.Mechanics.MapExtend
{
    internal static class MapExtendAuthorTemplates
    {
        private const string LayoutReadme =
            "\r\n统一安装规则：只需复制本次使用的功能文件夹，不必复制整套模板。\r\n" +
            "推荐 Mod根目录/EC2BUnofficialPatch/功能文件夹，其次 Mod根目录/功能文件夹，\r\n" +
            "也支持 Mod根目录/其他名称的容器/功能文件夹（可多层）。\r\n" +
            "移动整个功能文件夹并保留其内部结构，资源相对路径仍以配置所在目录为准。\r\n" +
            "模板生成位置不变；原版 Cfgs/zh-cn、Audios、Textures 仍遵循原版 Mod 注册及路径规则。\r\n";

        internal static void Ensure()
        {
            string root = Path.Combine(Paths.PluginPath, "EC2BUnofficialPatch");
            string directory = Path.Combine(root, "MapExtend");
            try
            {
                Directory.CreateDirectory(directory);
                Write(directory, "readme.txt", Read("Readme.txt") + LayoutReadme);
                Write(directory, "map-extend.html", Read("Tool.html"));
                Write(directory, "scene-objects.html", Read("SceneTool.html"));
                string legacy = Path.Combine(root, "MapLocations"), backup = Path.Combine(directory, "旧版资料");
                if (Directory.Exists(legacy) && !Directory.Exists(backup)) Directory.Move(legacy, backup);
            }
            catch (Exception exception)
            {
                PatchLog.Warning("地图扩展与优化模板写入失败：" + ModuleHost.GetReason(exception));
            }
        }
        private static void Write(string directory, string name, string text) =>
            File.WriteAllText(Path.Combine(directory, name), text, new UTF8Encoding(false));
        private static string Read(string suffix)
        {
            string name = "EC2BUnofficialPatch.Embedded.MapExtend." + suffix;
            using (Stream stream = typeof(MapExtendAuthorTemplates).Assembly.GetManifestResourceStream(name))
            {
                if (stream == null) throw new FileNotFoundException("找不到内嵌地图模板", name);
                using (var reader = new StreamReader(stream, Encoding.UTF8, true)) return reader.ReadToEnd();
            }
        }
    }
}
