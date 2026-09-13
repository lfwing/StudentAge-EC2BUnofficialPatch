namespace LFBetterAudio.Audio
{
    /// <summary>
    /// 与原版 AudioCfg 的主要分类保持一致：1=音乐，2=音效。
    /// 其他值在 BetterAudio 中统一按音乐处理。
    /// </summary>
    public enum BetterAudioType
    {
        Music = 1,
        SoundEffect = 2
    }

    public sealed class ResolvedAudio
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string AudioPath { get; set; }
        public string TimelinePath { get; set; }
        public float Volume { get; set; }
        public BetterAudioType AudioType { get; set; } = BetterAudioType.Music;
        public bool UsesExternalMusicFile { get; set; }
        public bool UsesOriginalGameAudio { get; set; }
    }
}
