using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace EC2BUnofficialPatch.Features.ScreenEffects.ScreenLyrcis
{
    internal sealed class LyricRegistry
    {
        private readonly Dictionary<int, RegisteredLyric> _lyrics =
            new Dictionary<int, RegisteredLyric>();
        private readonly List<LyricConflict> _conflicts =
            new List<LyricConflict>();
        private readonly List<string> _errors = new List<string>();
        private readonly HashSet<int> _conflictedIds = new HashSet<int>();

        internal IReadOnlyList<LyricConflict> Conflicts => _conflicts;
        internal IReadOnlyList<string> Errors => _errors;
        internal int Count => _lyrics.Count;

        internal static LyricRegistry Load(IReadOnlyList<string> files)
        {
            LyricRegistry registry = new LyricRegistry();
            foreach (string file in files)
            {
                registry.LoadFile(file);
            }

            return registry;
        }

        internal bool TryGet(int id, out LyricEntry entry)
        {
            entry = null;
            if (_conflictedIds.Contains(id) ||
                !_lyrics.TryGetValue(id, out RegisteredLyric registered))
            {
                return false;
            }

            entry = registered.Entry;
            return true;
        }

        private void LoadFile(string file)
        {
            LyricDatabase database;
            try
            {
                string json = File.ReadAllText(file);
                database = JsonConvert.DeserializeObject<LyricDatabase>(json);
                if (database?.lyrics == null)
                {
                    throw new InvalidDataException("根对象缺少 lyrics 数组。");
                }
            }
            catch (Exception exception)
            {
                _errors.Add($"无法读取歌词配置 {file}：{exception.Message}");
                return;
            }

            foreach (LyricEntry entry in database.lyrics)
            {
                if (entry == null || entry.id <= 0 || string.IsNullOrWhiteSpace(entry.text))
                {
                    _errors.Add($"歌词项必须包含正整数 id 和非空 text。source={file}");
                    continue;
                }

                if (entry.audio.HasValue && entry.audio.Value <= 0)
                {
                    _errors.Add(
                        $"歌词 id={entry.id} 的 audio 必须是正整数音乐 ID；省略该字段才表示无音乐。source={file}");
                    continue;
                }

                entry.text = entry.text.Replace("\\n", "\n");
                RegisteredLyric candidate = new RegisteredLyric(entry, file);
                if (_conflictedIds.Contains(entry.id))
                {
                    _errors.Add(
                        $"歌词 ID={entry.id} 已发生冲突，后续同 ID 项继续忽略。source={file}");
                    continue;
                }

                if (_lyrics.TryGetValue(entry.id, out RegisteredLyric selected))
                {
                    _lyrics.Remove(entry.id);
                    _conflictedIds.Add(entry.id);
                    _conflicts.Add(new LyricConflict(entry.id, selected, candidate));
                    continue;
                }

                _lyrics.Add(entry.id, candidate);
            }
        }
    }

    internal sealed class LyricConflict
    {
        internal LyricConflict(
            int id,
            RegisteredLyric selected,
            RegisteredLyric ignored)
        {
            Id = id;
            Selected = selected;
            Ignored = ignored;
        }

        internal int Id { get; }

        internal RegisteredLyric Selected { get; }

        internal RegisteredLyric Ignored { get; }
    }
}
