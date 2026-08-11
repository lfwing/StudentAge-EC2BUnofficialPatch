using System.Collections.Generic;

namespace EC2BUnofficialPatch.Core.Updates
{
    internal sealed class UpdateManifest
    {
        public int schema { get; set; }
        public string version { get; set; }
        public string channel { get; set; }
        public string gameVersion { get; set; }
        public string assetName { get; set; }
        public long size { get; set; }
        public string sha256 { get; set; }
        public string releasePage { get; set; }
        public List<string> downloadUrls { get; set; }
    }

    internal sealed class UpdateState
    {
        public string lastAttemptUtc { get; set; }
        public string lastManifestUrl { get; set; }
        public string lastSeenVersion { get; set; }
        public string scheduledVersion { get; set; }
    }
}
