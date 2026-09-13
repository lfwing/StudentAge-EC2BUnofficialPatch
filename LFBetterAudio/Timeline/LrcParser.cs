using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LFBetterAudio.Timeline
{
    public static class LrcParser
    {
        private static readonly Regex TimestampRegex = new Regex(
            @"\[(\d{1,3}):(\d{1,2})(?:[\.:](\d{1,3}))?\]",
            RegexOptions.Compiled);

        private static readonly Regex OffsetRegex = new Regex(
            @"^\s*\[offset:([+-]?\d+)\]\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex SingerPrefixRegex = new Regex(
            @"^\s*id(\d+)\s*(?=\[)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static List<LrcLine> ParseFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return new List<LrcLine>();
            }

            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            float offsetSeconds = 0f;

            foreach (string raw in lines)
            {
                Match offsetMatch = OffsetRegex.Match(raw ?? string.Empty);
                if (offsetMatch.Success &&
                    int.TryParse(
                        offsetMatch.Groups[1].Value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int offsetMs))
                {
                    offsetSeconds = offsetMs / 1000f;
                }
            }

            int sequence = 0;
            var sortable = new List<Tuple<LrcLine, int>>();

            foreach (string rawLine in lines)
            {
                string line = rawLine ?? string.Empty;
                int singerSlot = 0;

                Match singerMatch = SingerPrefixRegex.Match(line);
                if (singerMatch.Success)
                {
                    int.TryParse(
                        singerMatch.Groups[1].Value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out singerSlot);
                    line = line.Substring(singerMatch.Length);
                }

                MatchCollection matches = TimestampRegex.Matches(line);
                if (matches.Count == 0)
                {
                    // 网易云 JSON/NDJSON 元数据等无时间标签内容不计入歌词句数。
                    continue;
                }

                string lyricText = TimestampRegex.Replace(line, string.Empty).Trim();
                SplitBilingualText(lyricText, out string primary, out string secondary);

                foreach (Match match in matches)
                {
                    if (!TryParseTimestamp(match, out float seconds))
                    {
                        continue;
                    }

                    int sourceSequence = sequence++;
                    sortable.Add(Tuple.Create(new LrcLine
                    {
                        // u/v 的句号严格按文件中有效时间歌词从上到下生成；
                        // 播放列表稍后仍按时间排序，二者互不冲突。
                        LineNumber = sourceSequence + 1,
                        TimeSeconds = Math.Max(0f, seconds + offsetSeconds),
                        SingerSlot = Math.Max(0, singerSlot),
                        PrimaryText = primary,
                        SecondaryText = secondary
                    }, sourceSequence));
                }
            }

            var result = new List<LrcLine>();
            foreach (Tuple<LrcLine, int> item in sortable
                         .OrderBy(x => x.Item1.TimeSeconds)
                         .ThenBy(x => x.Item2))
            {
                result.Add(item.Item1);
            }

            return result;
        }

        private static void SplitBilingualText(
            string lyricText,
            out string primary,
            out string secondary)
        {
            lyricText = lyricText ?? string.Empty;
            int separatorIndex = lyricText.IndexOf('|');
            if (separatorIndex < 0)
            {
                primary = lyricText.Trim();
                secondary = string.Empty;
                return;
            }

            primary = lyricText.Substring(0, separatorIndex).Trim();
            secondary = lyricText.Substring(separatorIndex + 1).Trim();
        }

        private static bool TryParseTimestamp(Match match, out float seconds)
        {
            seconds = 0f;
            if (!int.TryParse(
                    match.Groups[1].Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int minutes) ||
                !int.TryParse(
                    match.Groups[2].Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int wholeSeconds))
            {
                return false;
            }

            float fraction = 0f;
            string fractionText = match.Groups[3].Success
                ? match.Groups[3].Value
                : string.Empty;
            if (fractionText.Length > 0 &&
                int.TryParse(
                    fractionText,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int fractionValue))
            {
                fraction = fractionValue / (float)Math.Pow(10d, fractionText.Length);
            }

            seconds = minutes * 60f + wholeSeconds + fraction;
            return true;
        }
    }
}
