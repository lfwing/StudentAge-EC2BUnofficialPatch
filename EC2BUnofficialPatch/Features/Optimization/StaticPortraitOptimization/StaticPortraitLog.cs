using System;
using EC2BUnofficialPatch.Core;

namespace EC2BUnofficialPatch.Features.Optimization.StaticPortraitOptimization
{
    internal static class StaticPortraitLog
    {
        private static bool Enabled => PluginConfig.StaticPortraitLogging?.Value == true;

        internal static void Debug(string message)
        {
            if (Enabled) PatchLog.Debug(message);
        }

        internal static void Warning(string message)
        {
            if (Enabled) PatchLog.Warning(message);
        }

        internal static void Error(string message)
        {
            if (Enabled) PatchLog.Error(message);
        }

        internal static void Exception(string context, Exception exception)
        {
            if (Enabled) PatchLog.Exception(context, exception);
        }
    }
}
