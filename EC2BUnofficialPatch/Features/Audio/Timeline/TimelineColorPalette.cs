using UnityEngine;

namespace LFBetterAudio.Timeline
{
    internal sealed class LyricsColorEntry
    {
        internal LyricsColorEntry(int id, string name, Color color)
        {
            Id = id;
            Name = name;
            Color = color;
        }

        internal int Id { get; }
        internal string Name { get; }
        internal Color Color { get; }
    }

    /// <summary>
    /// 背景型歌词的 32 色调色板。
    /// 0 固定为白色，1~31 按红、粉、橙、黄、绿、青蓝、紫的色系顺序排列。
    /// 唱歌模式的男女/合唱颜色使用独立内部编号，避免与作者可填写的 0~31 冲突。
    /// </summary>
    internal static class TimelineColorPalette
    {
        internal const int MinAuthorColorId = 0;
        internal const int MaxAuthorColorId = 31;
        internal const int MaleInternalColorId = 100;
        internal const int FemaleInternalColorId = 101;
        internal const int ChoirInternalColorId = 102;

        private static readonly LyricsColorEntry[] Entries =
        {
            // 基础色
            Entry(0,  "白色",   255, 255, 255),

            // 红色系
            Entry(1,  "正红",   230, 0, 18),
            Entry(2,  "朱红",   255, 77, 79),
            Entry(3,  "深红",   183, 28, 28),
            Entry(4,  "酒红",   142, 36, 77),

            // 粉色系
            Entry(5,  "珊瑚粉", 255, 127, 128),
            Entry(6,  "桃粉",   255, 183, 178),
            Entry(7,  "樱花粉", 255, 183, 197),
            Entry(8,  "玫瑰粉", 244, 114, 182),

            // 橙色系
            Entry(9,  "橙红",   255, 87, 34),
            Entry(10, "橙色",   255, 140, 0),
            Entry(11, "杏橙",   255, 179, 71),

            // 黄色系
            Entry(12, "金黄",   255, 215, 0),
            Entry(13, "明黄",   255, 235, 59),
            Entry(14, "柠檬黄", 255, 241, 118),
            Entry(15, "米黄",   244, 227, 161),

            // 绿色系
            Entry(16, "黄绿",   154, 205, 50),
            Entry(17, "青柠绿", 126, 217, 87),
            Entry(18, "草绿",   76, 175, 80),
            Entry(19, "翠绿",   0, 168, 107),
            Entry(20, "薄荷绿", 102, 221, 170),

            // 青蓝色系
            Entry(21, "青绿",   32, 178, 170),
            Entry(22, "青色",   0, 188, 212),
            Entry(23, "天蓝",   79, 195, 247),
            Entry(24, "湖蓝",   33, 150, 243),
            Entry(25, "宝蓝",   21, 101, 192),
            Entry(26, "靛蓝",   63, 81, 181),

            // 紫色系
            Entry(27, "蓝紫",   94, 53, 177),
            Entry(28, "淡紫",   179, 157, 219),
            Entry(29, "紫色",   156, 39, 176),
            Entry(30, "紫红",   194, 24, 91),
            Entry(31, "洋红",   233, 30, 99)
        };

        internal static int NormalizeAuthorColorId(int colorId)
        {
            return colorId >= MinAuthorColorId && colorId <= MaxAuthorColorId
                ? colorId
                : MinAuthorColorId;
        }

        internal static string GetName(int colorId)
        {
            int normalized = NormalizeAuthorColorId(colorId);
            return Entries[normalized].Name;
        }

        internal static Color Resolve(int colorId)
        {
            switch (colorId)
            {
                case MaleInternalColorId:
                    return new Color32(87, 199, 255, 255);
                case FemaleInternalColorId:
                    return new Color32(255, 122, 184, 255);
                case ChoirInternalColorId:
                    return new Color32(184, 122, 255, 255);
                default:
                    int normalized = NormalizeAuthorColorId(colorId);
                    return Entries[normalized].Color;
            }
        }

        private static LyricsColorEntry Entry(
            int id,
            string name,
            byte red,
            byte green,
            byte blue)
        {
            return new LyricsColorEntry(id, name, new Color32(red, green, blue, 255));
        }
    }
}
