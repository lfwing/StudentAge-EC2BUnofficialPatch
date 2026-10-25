using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EC2BUnofficialPatch.Core;
using EC2BUnofficialPatch.Features.Effects;
using HarmonyLib;
using Sdk;
using UnityEngine.InputSystem;

namespace EC2BUnofficialPatch.Features.Optimization
{
    internal sealed class ConsoleHistoryModule : IPluginModule
    {
        public string Key => "debug.console-history";
        public IReadOnlyList<ModuleLogItem> LogItems => new[] { new ModuleLogItem("Debug", "控制台调取历史记录") };
        public void Load(Harmony harmony, PluginServices services)
        {
            var p = typeof(ConsoleHistoryPatches);
            EffectExtensionData.Patch(harmony, typeof(DebugView), "AcceptInput", p, prefix: nameof(ConsoleHistoryPatches.Submit));
            EffectExtensionData.Patch(harmony, typeof(DebugView), "Update", p, postfix: nameof(ConsoleHistoryPatches.Update));
            EffectExtensionData.Patch(harmony, typeof(DebugView), "SetConsoleShow", p, postfix: nameof(ConsoleHistoryPatches.Show));
        }
    }

    internal sealed class ConsoleCommandHistory
    {
        public ConsoleCommandHistory() { }
        private readonly List<string> commands = new List<string>();
        private int cursor;
        private string draft = "";
        internal void Add(string command)
        {
            if (!string.IsNullOrWhiteSpace(command)) commands.Add(command);
            Reset();
        }
        internal void Reset() { cursor = commands.Count; draft = ""; }
        internal string Move(int direction, string current)
        {
            if (commands.Count == 0) return current;
            if (cursor == commands.Count) draft = current;
            cursor = System.Math.Max(0, System.Math.Min(commands.Count, cursor + direction));
            return cursor == commands.Count ? draft : commands[cursor];
        }
    }

    internal static class ConsoleHistoryPatches
    {
        private static readonly ConditionalWeakTable<DebugView, ConsoleCommandHistory> Histories =
            new ConditionalWeakTable<DebugView, ConsoleCommandHistory>();
        internal static void Submit(DebugView __instance)
        {
            if (DebugMgr.Ins != null && DebugMgr.Ins.isConsoleShowing && __instance.input_console != null)
                Histories.GetOrCreateValue(__instance).Add(__instance.input_console.text);
        }
        internal static void Show(DebugView __instance) => Histories.GetOrCreateValue(__instance).Reset();
        internal static void Update(DebugView __instance)
        {
            var input = __instance.input_console;
            var keyboard = Keyboard.current;
            if (keyboard == null || input == null || !input.isFocused || DebugMgr.Ins == null || !DebugMgr.Ins.isConsoleShowing) return;
            int direction = keyboard.upArrowKey.wasPressedThisFrame ? -1 : keyboard.downArrowKey.wasPressedThisFrame ? 1 : 0;
            if (direction == 0) return;
            input.text = Histories.GetOrCreateValue(__instance).Move(direction, input.text);
            input.MoveTextEnd(false);
        }
    }
}
