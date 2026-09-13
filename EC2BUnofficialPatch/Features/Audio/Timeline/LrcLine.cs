namespace LFBetterAudio.Timeline
{
    public sealed class LrcLine
    {
        /// <summary>按有效歌词从上到下生成的 1 基句号。</summary>
        public int LineNumber { get; set; }

        public float TimeSeconds { get; set; }

        /// <summary>时间标签前的 idN；0 表示没有标注。</summary>
        public int SingerSlot { get; set; }

        public string PrimaryText { get; set; }
        public string SecondaryText { get; set; }

        /// <summary>兼容旧调用：双语时用换行拼接。</summary>
        public string Text
        {
            get
            {
                if (string.IsNullOrEmpty(SecondaryText))
                {
                    return PrimaryText ?? string.Empty;
                }
                return (PrimaryText ?? string.Empty) + "\n" + SecondaryText;
            }
        }
    }
}
