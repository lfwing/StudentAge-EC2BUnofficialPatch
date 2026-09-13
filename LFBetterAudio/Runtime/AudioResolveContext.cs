using System.Collections.Generic;
using Config;

namespace LFBetterAudio.Runtime
{
    public sealed class AudioResolveContext
    {
        public TalkChannel Channel { get; set; }
        public Dictionary<int, AudioCfg> PreviewAudioCfgMap { get; set; }
    }
}
