// CLR-only fixture: preserve game code; make unavailable Unity ECalls explicit.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class PrepareEffectTestRuntime
{
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, request) =>
            Assembly.LoadFrom(Path.Combine(args[2], new AssemblyName(request.Name).Name + ".dll"));
        Prepare(args[0], args[1]);
        return 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Prepare(string input, string output)
    {
        using (var module = ModuleDefinition.ReadModule(input))
        {
            int count = 0;
            foreach (var type in module.Types.SelectMany(t => new[] { t }.Concat(t.NestedTypes)))
            foreach (var method in type.Methods.Where(m => m.IsInternalCall).ToArray())
            {
                method.ImplAttributes = Mono.Cecil.MethodImplAttributes.IL | Mono.Cecil.MethodImplAttributes.Managed;
                method.Body = new Mono.Cecil.Cil.MethodBody(method);
                var il = method.Body.GetILProcessor();
                string name = method.Name == "Ceil" || method.Name == "CeilToInt" ? "Ceiling" :
                    method.Name == "FloorToInt" ? "Floor" : method.Name == "RoundToInt" ? "Round" : method.Name;
                var math = typeof(Math).GetMethods().FirstOrDefault(m => m.Name == name &&
                    m.ReturnType == typeof(double) && m.GetParameters().Length == method.Parameters.Count &&
                    m.GetParameters().All(p => p.ParameterType == typeof(double)));
                if (type.FullName == "UnityEngine.Mathf" && method.IsStatic && math != null &&
                    method.Parameters.All(p => p.ParameterType.MetadataType == MetadataType.Single))
                {
                    for (int i = 0; i < method.Parameters.Count; i++)
                    {
                        il.Append(il.Create(OpCodes.Ldarg, method.Parameters[i]));
                        il.Append(il.Create(OpCodes.Conv_R8));
                    }
                    il.Append(il.Create(OpCodes.Call, module.ImportReference(math)));
                    il.Append(il.Create(method.ReturnType.MetadataType == MetadataType.Int32 ? OpCodes.Conv_I4 : OpCodes.Conv_R4));
                    il.Append(il.Create(OpCodes.Ret));
                }
                else if (type.FullName == "UnityEngine.DebugLogHandler" && method.ReturnType.MetadataType == MetadataType.Void)
                {
                    // Logging has no engine backend in this fixture.
                    il.Append(il.Create(OpCodes.Ret));
                }
                else
                {
                    il.Append(il.Create(OpCodes.Ldstr, "Unity API unavailable in numerical fixture: " + method.FullName));
                    il.Append(il.Create(OpCodes.Newobj, module.ImportReference(typeof(NotSupportedException).GetConstructor(new[] { typeof(string) }))));
                    il.Append(il.Create(OpCodes.Throw));
                }
                count++;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            module.Write(output);
            Console.WriteLine("CLR fixture: " + count + " Unity ECalls isolated; Mathf uses System.Math; other engine APIs throw.");
        }
    }
}
