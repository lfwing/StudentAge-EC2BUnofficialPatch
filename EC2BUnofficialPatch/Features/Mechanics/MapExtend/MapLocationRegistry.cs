using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EC2BUnofficialPatch.Workshop;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EC2BUnofficialPatch.Features.Mechanics.MapExtend
{
    internal sealed class MapLocationEntry
    {
        internal int MapId;
        internal int WorldId;
        internal int Priority;
        internal ulong ModId;
        internal bool LocalContent;
        internal bool ShowLabel;
        internal string Label;
        internal string ImagePath;
        internal string HoverImagePath;
        internal string Source;
        internal Vector2 Position;
        internal Vector2 Size;
        internal Vector2 ImagePosition;
        internal Vector2 ImageSize;
        internal Vector2 HoverPosition;
        internal Vector2 HoverSize;
        internal Vector2 LabelPosition;
        internal Vector2 LabelSize;
        internal float LabelScale;
        internal Color LabelColor;
        internal Color TextColor;
        internal Color OutlineColor;
        internal float OutlineWidth;
        internal float AlphaThreshold;
        internal Vector2[] Polygon;
    }

    internal sealed class MapLocationRegistry
    {
        internal const string Folder = "MapExtend";
        internal const string FileName = "MapLocationscfg.json";
        internal readonly List<MapLocationEntry> Entries = new List<MapLocationEntry>();
        internal int FileCount;

        // Parse the whole replacement before switching it. No Workshop files are changed.
        internal static MapLocationRegistry Load(IReadOnlyList<ContentRoot> roots)
        {
            var result = new MapLocationRegistry();
            foreach (ContentRoot root in WorldMapsContent.Sources(roots))
            {
                string path = FeatureContentDiscovery.ChildFile(root.Path, FileName);
                if (!File.Exists(path)) continue;
                result.FileCount++;
                var ids = new HashSet<int>();
                JObject document = JObject.Parse(File.ReadAllText(path));
                if (document.Value<int?>("schema") != 2 || !(document["locations"] is JArray locations))
                    throw new InvalidDataException(path + ": 需要 schema=2 和 locations 数组。");
                foreach (JToken token in locations)
                {
                    if (!(token is JObject row)) throw new InvalidDataException(path + ": 地点必须为对象。");
                    try
                    {
                        MapLocationEntry entry = Parse(row, path);
                        entry.ModId = WorldMapsContent.ModId(root);
                        entry.LocalContent = root.Id == "local-worlds";
                        if (!ids.Add(entry.MapId))
                            throw new InvalidDataException("同一 mapId 重复登记：" + entry.MapId);
                        result.Entries.Add(entry);
                    }
                    catch (Exception exception)
                    {
                        throw new InvalidDataException(path + ": " + exception.Message, exception);
                    }
                }
            }
            return result;
        }

        internal static MapLocationEntry Parse(JObject row, string source)
        {
            JObject hit = ObjectField(row, "hitArea", true);
            JObject visual = ObjectField(row, "visual");
            JObject label = ObjectField(row, "label");
            JObject fallback = ObjectField(visual, "fallbackOutline");
            var entry = new MapLocationEntry
            {
                MapId = row.Value<int?>("mapId") ?? 0,
                WorldId = row.Value<int?>("worldId") ?? 0,
                Priority = row.Value<int?>("priority") ?? 3,
                ShowLabel = label.Value<bool?>("visible") ?? true,
                Label = label.Value<string>("text"),
                Source = source,
                LabelSize = ReadPoint(label["size"], Vector2.zero),
                LabelScale = ReadNumber(label, "scale", 1),
                LabelColor = ReadColor(label, "backgroundColor", "#00BFE8FF"),
                TextColor = ReadColor(label, "textColor", "#FFFFFFFF"),
                OutlineColor = ReadColor(fallback, "color", "#FFE89FFF"),
                OutlineWidth = ReadNumber(fallback, "width", 3),
                AlphaThreshold = ReadNumber(hit, "alphaThreshold", 0.1f)
            };
            if (entry.WorldId < 0 || entry.MapId <= 1 || entry.Priority < 1)
                throw new InvalidDataException("mapId 必须大于 1，priority 至少为 1。");
            if (entry.OutlineWidth <= 0 || entry.OutlineWidth > 32 ||
                entry.AlphaThreshold <= 0 || entry.AlphaThreshold > 1 || entry.LabelScale <= 0 || entry.LabelScale > 10 ||
                (label["size"] != null && (entry.LabelSize.x <= 0 || entry.LabelSize.y <= 0)))
                throw new InvalidDataException("标签尺寸/缩放、轮廓线宽或透明度阈值无效。");
            ReadVisual(visual, "normal", source, out entry.ImagePath, out entry.ImagePosition, out entry.ImageSize);
            ReadVisual(visual, "hover", source, out entry.HoverImagePath, out entry.HoverPosition, out entry.HoverSize);
            string type = hit.Value<string>("type") ?? "polygon";
            if (type == "polygon")
            {
                if (!(hit["points"] is JArray points) || points.Count < 3 || points.Count > 256)
                    throw new InvalidDataException("hitArea.points 需要 3～256 个顶点。");
                Vector2[] polygon = points.Select(point => ReadPoint(point, Vector2.zero)).ToArray();
                Vector2 min = polygon[0], max = polygon[0];
                foreach (Vector2 point in polygon) { min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
                entry.Position = (min + max) * 0.5f;
                entry.Size = max - min;
                if (entry.Size.x <= 0 || entry.Size.y <= 0) throw new InvalidDataException("命中区域面积为零。");
                entry.Polygon = polygon.Select(point => point - entry.Position).ToArray();
                MapLocationGeometry.Validate(entry.Polygon);
            }
            else if (type == "alpha" && entry.ImagePath != null)
            {
                // Alpha hit testing uses only the normal image, never the hover decoration.
                entry.Position = entry.ImagePosition;
                entry.Size = entry.ImageSize;
            }
            else throw new InvalidDataException("hitArea.type 必须为 polygon，或提供 normal 图片的 alpha。");
            entry.LabelPosition = ReadPoint(label["position"], entry.Position);
            return entry;
        }

        private static JObject ObjectField(JObject parent, string name, bool required = false)
        {
            JToken token = parent[name];
            if (token == null || token.Type == JTokenType.Null)
            {
                if (required) throw new InvalidDataException("缺少 " + name + " 对象。");
                return new JObject();
            }
            return token as JObject ?? throw new InvalidDataException(name + " 必须为对象。");
        }

        private static void ReadVisual(JObject visual, string name, string source,
            out string path, out Vector2 position, out Vector2 size)
        {
            JObject layer = ObjectField(visual, name);
            path = ResolveImage(source, layer.Value<string>("image"));
            position = size = Vector2.zero;
            if (path == null) return;
            if (layer["position"] == null || layer["size"] == null)
                throw new InvalidDataException(name + " 图片需要独立的 position 和 size。");
            position = ReadPoint(layer["position"], Vector2.zero);
            size = ReadPoint(layer["size"], Vector2.zero);
            if (size.x <= 0 || size.y <= 0) throw new InvalidDataException(name + ".size 必须为正。");
        }

        private static Vector2 ReadPoint(JToken token, Vector2 fallback)
        {
            if (token == null) return fallback;
            if (!(token is JArray point) || point.Count != 2 || !IsNumber(point[0]) || !IsNumber(point[1]))
                throw new InvalidDataException("坐标必须为两个有限数字组成的数组。");
            float x = point[0].Value<float>(), y = point[1].Value<float>();
            if (!Finite(x) || !Finite(y)) throw new InvalidDataException("坐标必须为有限数字。");
            return new Vector2(x, y);
        }

        private static bool IsNumber(JToken token) => token.Type == JTokenType.Integer || token.Type == JTokenType.Float;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static float ReadNumber(JObject row, string name, float fallback)
        {
            if (row[name] == null) return fallback;
            if (!IsNumber(row[name])) throw new InvalidDataException(name + " 必须为数字。");
            float value = row[name].Value<float>();
            if (!Finite(value)) throw new InvalidDataException(name + " 必须为有限数字。");
            return value;
        }

        private static Color ReadColor(JObject row, string name, string fallback)
        {
            string value = row.Value<string>(name) ?? fallback;
            // Documented hex colors can be parsed without Unity's native runtime.
            if (value.StartsWith("#", StringComparison.Ordinal) && (value.Length == 7 || value.Length == 9) &&
                uint.TryParse(value.Substring(1), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out uint rgba))
            {
                if (value.Length == 7) rgba = (rgba << 8) | 255;
                return new Color((rgba >> 24) / 255f, ((rgba >> 16) & 255) / 255f,
                    ((rgba >> 8) & 255) / 255f, (rgba & 255) / 255f);
            }
            if (!ColorUtility.TryParseHtmlString(value, out Color color))
                throw new InvalidDataException(name + " 必须为 #RRGGBB 或 #RRGGBBAA。");
            return color;
        }

        private static string ResolveImage(string config, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative)) return null;
            if (Path.IsPathRooted(relative)) throw new InvalidDataException("图片必须为注册表目录内的相对路径。");
            string directory = Path.GetFullPath(Path.GetDirectoryName(config)) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(directory, relative));
            if (!full.StartsWith(directory, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetExtension(full), ".png", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("PNG 路径越界或不是 PNG：" + relative);
            return full;
        }
    }

    internal static class MapLocationGeometry
    {
        internal static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        internal static bool Contains(Vector2[] polygon, Vector2 point)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[j], b = polygon[i];
                Vector2 edge = b - a;
                if (Mathf.Abs(Cross(edge, point - a)) < 0.001f &&
                    Vector2.Dot(point - a, point - b) <= 0.001f) return true;
                if ((a.y > point.y) != (b.y > point.y) &&
                    point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        internal static void Validate(Vector2[] points)
        {
            int count = points.Length;
            float area = 0;
            for (int i = 0; i < count; i++)
            {
                Vector2 a = points[i], b = points[(i + 1) % count];
                if ((a - b).sqrMagnitude < 0.0001f) throw new InvalidDataException("轮廓存在重复顶点。");
                area += Cross(a, b);
                for (int j = i + 1; j < count; j++)
                {
                    if (j == i + 1 || (i == 0 && j == count - 1)) continue;
                    Vector2 c = points[j], d = points[(j + 1) % count];
                    if (Intersects(a, b, c, d)) throw new InvalidDataException("轮廓不能自交或自接触。");
                }
            }
            if (Mathf.Abs(area) < 0.001f) throw new InvalidDataException("轮廓面积为零。");
        }

        private static bool Intersects(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float abC = Cross(b - a, c - a), abD = Cross(b - a, d - a);
            float cdA = Cross(d - c, a - c), cdB = Cross(d - c, b - c);
            if (abC * abD < 0 && cdA * cdB < 0) return true;
            return OnSegment(a, b, c, abC) || OnSegment(a, b, d, abD) ||
                   OnSegment(c, d, a, cdA) || OnSegment(c, d, b, cdB);
        }

        private static bool OnSegment(Vector2 a, Vector2 b, Vector2 p, float cross) =>
            Mathf.Abs(cross) < 0.0001f && Vector2.Dot(p - a, p - b) <= 0;
    }
}
