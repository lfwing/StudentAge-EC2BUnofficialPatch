namespace EC2BUnofficialPatch.Services
{
    /// <summary>Resolve a CFG ID with its originating Workshop mod. Never broadcasts an unqualified ID.</summary>
    public static class EventIdCompatibilityApi
    {
        public static bool Applied => EventCompatibility.EventIdCompatibilityService.Applied;

        /// <param name="table">EvtCfg, TalkCfg or OptionCfg.</param>
        public static int Resolve(ulong modId, string table, int originalId) =>
            EventCompatibility.EventIdCompatibilityService.Resolve(modId, table, originalId);
    }
}
