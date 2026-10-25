namespace LFBetterAudio.Discovery
{
    /// <summary>
    /// 一个可被 BetterAudio 读取的资源包。对于创意工坊 Mod，BetterAudioDirectory
    /// 指向实际发现的 BetterAudio 功能目录（允许任意容器）；ModRootDirectory 始终是所属 Mod 根目录。
    /// </summary>
    public sealed class BetterAudioPackage
    {
        public string ModRootDirectory { get; set; }
        public string BetterAudioDirectory { get; set; }
        public string ConfigPath { get; set; }
        public string SourceLabel { get; set; }
        public string WorkshopItemId { get; set; }
        public bool IsWorkshopPackage { get; set; }
    }
}
