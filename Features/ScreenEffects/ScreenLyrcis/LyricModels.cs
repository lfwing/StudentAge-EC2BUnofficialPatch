using System.Collections.Generic;

namespace EC2BUnofficialPatch.Features.ScreenEffects.ScreenLyrcis
{
    internal sealed class LyricDatabase
    {
        public List<LyricEntry> lyrics { get; set; } = new List<LyricEntry>();
    }

    internal sealed class LyricEntry
    {
        public int id { get; set; }
        public int? audio { get; set; }
        public string text { get; set; }
        public float fontSize { get; set; }
        public float lineSpacing { get; set; }
        public string fontColor { get; set; }
        public int alignH { get; set; }
        public int alignV { get; set; }

        internal bool PreserveExistingStyle { get; set; }
    }

    internal sealed class RegisteredLyric
    {
        internal RegisteredLyric(LyricEntry entry, string sourcePath)
        {
            Entry = entry;
            SourcePath = sourcePath;
        }

        internal LyricEntry Entry { get; }

        internal string SourcePath { get; }
    }
}
