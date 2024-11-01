using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using MonoMod.Cil;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TurboMode.Prepatch
{
    public class MiscCleanups : TurboModePrepatch
    {
        public static IEnumerable<string> TargetDLLs { get; private set; } = new[] { "Assembly-CSharp.dll" };

        private static bool enabled;

        public static void Initialize()
        {
            InitSharedResources();

            // Can't get this from TurboModePlugin, because we must avoid loading Assembly-CSharp.dll.
            enabled = config.Bind(
                "General",
                "MiscCleanups",
                true,
                "Miscellaneous small garbage cleanups and performance improvements."
            ).Value;

            if (!enabled)
            {
                logSource.LogInfo("MiscCleanups option is disabled. Skipping preload patching.");
                TargetDLLs = new string[0];
            }
        }

        public static void Patch(ref AssemblyDefinition assembly)
        {
            Patch_MessageCenter_RecycleMessage(assembly);
            CreateGetHashCode(assembly, "KSP.Sim.ResourceSystem.ResourceFlowRequestManager/RequestContainerGroupKey");
            CreateGetHashCode(assembly, "KSP.Sim.ResourceSystem.ResourceFlowRequestManager/RequestPriorityContainerGroupKey");
            MakeIEquatable(assembly, "KSP.Sim.ResourceSystem.ResourceFlowRequestManager/RequestContainerGroupKey");
            MakeIEquatable(assembly, "KSP.Sim.ResourceSystem.ResourceFlowRequestManager/RequestPriorityContainerGroupKey");
        }

        private static void Patch_MessageCenter_RecycleMessage(AssemblyDefinition assembly)
        {
            var targetMethod = assembly
                .MainModule.GetType("KSP.Messages.MessageCenter")
                .Methods.First(method => method.Name == "RecycleMessage");

            var replacement = assembly.MainModule.ImportReference(
                tmAssembly
                .MainModule.GetType("TurboMode.Patches.MiscCleanups")
                .Methods.First(method => method.Name == "ClearMessage")
            );

            ILContext context = new(targetMethod);
            ILCursor cursor = new(context);

            cursor.GotoNext(
                x => x.MatchCallOrCallvirt("KSP.Messages.MessageCenterMessage", "Clear")
            );
            cursor.Remove();
            cursor.Emit(OpCodes.Call, replacement);
        }

        private static void CreateGetHashCode(AssemblyDefinition assembly, string typeName)
        {
            var targetType = assembly
                .MainModule.GetType(typeName);

            MethodReference objectGetHashCode = assembly.MainModule.ImportReference(typeof(object).GetMethod("GetHashCode"));

            logSource.LogInfo($"Adding GetHashCode to {targetType}");

            MethodDefinition getHashCode = new(
                "GetHashCode",
                MethodAttributes.Virtual | MethodAttributes.Public,
                assembly.MainModule.ImportReference(typeof(int))
            );

            var intVar = new VariableDefinition(assembly.MainModule.ImportReference(typeof(int)));

            getHashCode.Body.Variables.Add(intVar);

            ILContext context = new(getHashCode);
            ILCursor cursor = new(context);

            cursor.Emit(OpCodes.Ldc_I4_0);
            cursor.Emit(OpCodes.Stloc_0);

            foreach (var field in targetType.Fields)
            {
                cursor.Emit(OpCodes.Ldloc_0);
                cursor.Emit(OpCodes.Ldarg_0);

                if (field.FieldType.IsValueType)
                {
                    cursor.Emit(OpCodes.Ldflda, field);
                    cursor.Emit(OpCodes.Constrained, field.FieldType);
                }
                else
                {
                    cursor.Emit(OpCodes.Ldfld, field);
                }

                cursor.Emit(OpCodes.Callvirt, objectGetHashCode);
                cursor.Emit(OpCodes.Add);
                cursor.Emit(OpCodes.Stloc_0);
            }
            cursor.Emit(OpCodes.Ldloc_0);
            cursor.Emit(OpCodes.Ret);

            targetType.Methods.Add(getHashCode);
        }

        private static void MakeIEquatable(AssemblyDefinition assembly, string typeName)
        {
            var targetType = assembly
                .MainModule.GetType(typeName);

            // Add IEquatable<T> to the type.
            // Needed to get Dictionary<K,V> to use our Equals() when the type is used as a key.
            TypeReference iEquatableType = assembly.MainModule.ImportReference(typeof(IEquatable<>));
            GenericInstanceType iEquatableOfTargetType = iEquatableType.MakeGenericInstanceType(targetType);
            InterfaceImplementation iEquatable = new(iEquatableOfTargetType);
            targetType.Interfaces.Add(iEquatable);

            MethodDefinition equals = new(
                "Equals",
                MethodAttributes.Public | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.Virtual | MethodAttributes.NewSlot,
                assembly.MainModule.ImportReference(typeof(bool))
            );

            // create "other" parameter
            ParameterDefinition otherParam = new("other", ParameterAttributes.None, targetType);
            equals.Parameters.Add(otherParam);
            // bool var to aggregate comparisons
            var boolVar = new VariableDefinition(assembly.MainModule.ImportReference(typeof(bool)));
            equals.Body.Variables.Add(boolVar);

            ILContext context = new(equals);
            ILCursor cursor = new(context);

            // initialize bool var
            cursor.Emit(OpCodes.Ldc_I4_1);
            cursor.Emit(OpCodes.Stloc_0);

            foreach (var field in targetType.Fields)
            {
                cursor.Emit(OpCodes.Ldloc_0);
                cursor.Emit(OpCodes.Ldarg_0);
                cursor.Emit(OpCodes.Ldfld, field);
                cursor.Emit(OpCodes.Ldarg_1);
                cursor.Emit(OpCodes.Ldfld, field);
                if (field.FieldType.IsValueType)
                {
                    // Weird, but have to make sure both the type and the method are imported.
                    var equalsOperatorDef = assembly.MainModule.ImportReference(field.FieldType).Resolve().FindMethod("op_Equality");
                    var equalsOperator = assembly.MainModule.ImportReference(equalsOperatorDef);
                    cursor.Emit(OpCodes.Call, equalsOperator);
                }
                else
                {
                    cursor.Emit(OpCodes.Ceq);
                }
                cursor.Emit(OpCodes.And);
                cursor.Emit(OpCodes.Stloc_0);
            }

            cursor.Emit(OpCodes.Ldloc_0);
            cursor.Emit(OpCodes.Ret);

            targetType.Methods.Add(equals);
            logSource.LogInfo($"Created {equals}");
        }
    }
}
