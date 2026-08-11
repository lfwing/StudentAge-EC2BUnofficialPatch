using System;
using System.Collections.Generic;
using BepInEx.Logging;

namespace EC2BUnofficialPatch.Core
{
    internal static class PatchLog
    {
        private static ManualLogSource _logger;
        private static readonly List<string> RegistrationResults = new List<string>();
        private static int _issueCount;
        private static bool _registrationsFlushed;

        internal static void Initialize(ManualLogSource logger)
        {
            _logger = logger;
            RegistrationResults.Clear();
            _issueCount = 0;
            _registrationsFlushed = false;
        }
        internal static void Info(string message) => _logger?.LogInfo(message);
        internal static void Debug(string message) => _logger?.LogDebug(message);
        internal static void Warning(string message)
        {
            _issueCount++;
            _logger?.LogWarning(message);
        }
        internal static void Error(string message)
        {
            _issueCount++;
            _logger?.LogError(message);
        }
        internal static void Exception(string context, Exception exception)
        {
            _issueCount++;
            _logger?.LogError($"{context}：{ModuleHost.GetReason(exception)}\n{exception}");
        }

        internal static int IssueCount => _issueCount;

        internal static void Registration(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;
            if (_registrationsFlushed)
            {
                _logger?.LogInfo(message);
                return;
            }
            RegistrationResults.Add(message);
        }

        internal static void FlushRegistrations()
        {
            foreach (string result in RegistrationResults)
                _logger?.LogInfo(result);
            RegistrationResults.Clear();
            _registrationsFlushed = true;
        }

        internal static int RegistrationCheckpoint() => RegistrationResults.Count;

        internal static void RollbackRegistrations(int checkpoint)
        {
            if (checkpoint < 0 || checkpoint >= RegistrationResults.Count)
                return;
            RegistrationResults.RemoveRange(checkpoint, RegistrationResults.Count - checkpoint);
        }
    }
}
