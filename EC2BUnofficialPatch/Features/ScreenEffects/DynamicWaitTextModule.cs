using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using EC2BUnofficialPatch.Core;
using HarmonyLib;
using View.Evt;

namespace EC2BUnofficialPatch.Features.ScreenEffects
{
    internal sealed class DynamicWaitTextModule : IPluginModule
    {
        private static readonly ConditionalWeakTable<object, WaitTextState> States =
            new ConditionalWeakTable<object, WaitTextState>();

        private static readonly MethodInfo CaptureMethod =
            AccessTools.Method(typeof(DynamicWaitTextModule), nameof(CaptureCommand));
        private static readonly MethodInfo ResetSequenceMethod =
            AccessTools.Method(typeof(DynamicWaitTextModule), nameof(ResetSequence));
        private static readonly MethodInfo TranspilerMethod =
            AccessTools.Method(typeof(DynamicWaitTextModule), nameof(ReplaceDefaultTextId));
        private static readonly MethodInfo ResolveTextIdMethod =
            AccessTools.Method(typeof(DynamicWaitTextModule), nameof(ResolveTextId));

        private static readonly IReadOnlyList<ModuleLogItem> Items = new[]
        {
            new ModuleLogItem("屏幕特效", "4006-黑屏过场显示文字扩展")
        };

        public string Key => "screen.4006";

        public IReadOnlyList<ModuleLogItem> LogItems => Items;

        public void Load(Harmony harmony, PluginServices services)
        {
            PatchTalkView(harmony, typeof(NewTalkView));
            PatchTalkView(harmony, typeof(PreviewTalkView));
        }

        private static void PatchTalkView(Harmony harmony, Type talkViewType)
        {
            TalkViewAccessor.Validate(talkViewType);

            MethodInfo playScreenEffect = RequireMethod(talkViewType, "PlayScreenEffect");
            MethodInfo waitBackground = RequireMethod(talkViewType, "WaitBg");

            harmony.Patch(
                playScreenEffect,
                prefix: new HarmonyMethod(CaptureMethod));
            harmony.Patch(
                waitBackground,
                prefix: new HarmonyMethod(ResetSequenceMethod),
                transpiler: new HarmonyMethod(TranspilerMethod));
        }

        private static void CaptureCommand(object __instance)
        {
            List<float> screenEffect = TalkViewAccessor.GetScreenEffect(__instance);
            if (screenEffect == null ||
                screenEffect.Count == 0 ||
                (int)screenEffect[0] != CommandIds.DynamicWaitText)
            {
                return;
            }

            int textId =
                screenEffect.Count > 1
                    ? (int)screenEffect[1]
                    : CommandIds.DefaultWaitText;
            States.GetOrCreateValue(__instance).TextId = textId;
            PatchLog.Info($"屏幕特效模块-4006黑屏文字调用：textId={textId}");
        }

        private static void ResetSequence(object __instance)
        {
            TalkViewAccessor.KillWaitSequence(__instance);
        }

        private static IEnumerable<CodeInstruction> ReplaceDefaultTextId(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> rewritten = new List<CodeInstruction>();
            int replacementCount = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (!LoadsInteger(instruction, CommandIds.DefaultWaitText))
                {
                    rewritten.Add(instruction);
                    continue;
                }

                instruction.opcode = OpCodes.Ldarg_0;
                instruction.operand = null;
                rewritten.Add(instruction);
                rewritten.Add(new CodeInstruction(OpCodes.Call, ResolveTextIdMethod));
                replacementCount++;
            }

            if (replacementCount != 1)
            {
                throw new InvalidOperationException(
                    $"WaitBg 中预期替换 1 处文本 ID，实际找到 {replacementCount} 处。");
            }

            return rewritten;
        }

        private static int ResolveTextId(object talkView)
        {
            WaitTextState state = States.GetOrCreateValue(talkView);
            int textId = state.TextId;
            state.TextId = CommandIds.DefaultWaitText;
            return textId;
        }

        private static bool LoadsInteger(CodeInstruction instruction, int value)
        {
            if (instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int operand)
            {
                return operand == value;
            }

            return false;
        }

        private static MethodInfo RequireMethod(Type type, string name)
        {
            return AccessTools.Method(type, name)
                ?? throw new MissingMethodException(type.FullName, name);
        }

        private sealed class WaitTextState
        {
            internal int TextId = CommandIds.DefaultWaitText;
        }
    }
}
