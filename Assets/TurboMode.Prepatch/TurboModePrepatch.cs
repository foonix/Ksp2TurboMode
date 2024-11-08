using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using System.IO;

namespace TurboMode.Prepatch
{
    public abstract class TurboModePrepatch
    {
        protected static ManualLogSource logSource;
        protected static ConfigFile config;
        protected static AssemblyDefinition tmAssembly;
        private static bool isInitialized;

        protected static void InitSharedResources()
        {
            if (isInitialized) return;

            logSource = Logger.CreateLogSource("TurboMode.Preload");
            logSource.LogInfo("BurstMath Initialize()");

            var configPath = Utility.CombinePaths(Paths.ConfigPath, "TurboMode.cfg");
            config = new ConfigFile(configPath, saveOnInit: false)
            {
                // We have to Bind() here to get the value, but avoid writing because we don't have all of the settings.
                SaveOnConfigSet = false
            };

            // Get our main assembly signatures without actually loading it.
            var thisAsmPath = typeof(BurstMathInjector).Assembly.Location;
            tmAssembly = AssemblyDefinition.ReadAssembly(Path.Combine(Path.GetDirectoryName(thisAsmPath), "..", "plugins", "TurboMode", "TurboMode.dll"));


            isInitialized = true;
        }

        protected static void CleanupSharedResources()
        {
            if (!isInitialized) return;

            // free up unused
            config = null;
            logSource = null;
            tmAssembly = null;

            isInitialized = false;
        }

        // there is probably a helper for this somewhere in cecil, but can't find it.
        protected static ILCursor EmitLdarg(int index, ILCursor cursor) => index switch
        {
            0 => cursor.Emit(OpCodes.Ldarg_0),
            1 => cursor.Emit(OpCodes.Ldarg_1),
            2 => cursor.Emit(OpCodes.Ldarg_2),
            3 => cursor.Emit(OpCodes.Ldarg_3),
            _ => cursor.Emit(OpCodes.Ldarg_S, (byte)index),
        };

        protected static void DebugDumpMethod(MethodDefinition method)
        {
            logSource.LogInfo($"Debug dump of {method.FullName}");
            logSource.LogInfo("Parameters:");
            foreach (var param in method.Parameters)
            {
                logSource.LogInfo($"  {param.ParameterType} {param.Name}");
            }
            logSource.LogInfo("Locals:");
            foreach (var variable in method.Body.Variables)
            {
                logSource.LogInfo($"  {variable.VariableType}");
            }
            logSource.LogInfo("Instructions:");
            foreach (var instr in method.Body.Instructions)
            {
                logSource.LogInfo($"  {instr}");

            }
        }

        protected static void DebugDumpMethod(MethodReference method)
        {
            logSource.LogInfo($"Debug dump of reference to {method.FullName}");
            logSource.LogInfo("Parameters:");
            foreach (var param in method.Parameters)
            {
                logSource.LogInfo($"  {param.ParameterType} {param.Name}");
            }
        }

        protected static void OverrideBodyWithCallTo(MethodDefinition inMethod, MethodReference callToMethod)
        {
            logSource.LogInfo($"Patching body of {inMethod.FullName} to only call {callToMethod.FullName}");

            ILContext context = new(inMethod);
            ILCursor cursor = new(context);

            // I couldn't get clearing the body to work, so just stuff the call and return before existing code.
            cursor.Goto(0);

            cursor.Emit(OpCodes.Ldarg_0); // `this`
            // copy parameters to stack
            int i = 0;
            foreach (var paramter in inMethod.Parameters)
            {
                EmitLdarg(i + 1, cursor);

                i++;
            }

            cursor.Emit(OpCodes.Call, callToMethod);
            cursor.Emit(OpCodes.Ret);

            DebugDumpMethod(inMethod);
            DebugDumpMethod(callToMethod);
        }
    }
}
