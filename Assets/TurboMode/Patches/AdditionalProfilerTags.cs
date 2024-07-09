using HarmonyLib;
using KSP.Sim.Definitions;
using KSP.Sim.impl;
using Unity.Profiling;

namespace TurboMode
{
    public static class AdditionalProfilerTags
    {
        static readonly ProfilerMarker PartBehaviour_OnFixedUpdate = new("PartBehaviour.OnFixedUpdate");
        static readonly ProfilerMarker PartBehaviourModule_OnFixedUpdate = new("PartBehaviourModule.OnFixedUpdate");
        static readonly ProfilerMarker PhysicsSpaceProvider_OnFixedUpdate = new("PhysicsSpaceProvider.OnFixedUpdate");
        static readonly ProfilerMarker RigidbodyBehavior_OnFixedUpdate = new("RigidbodyBehavior.OnFixedUpdate");
        static readonly ProfilerMarker SpaceSimulation_OnFixedUpdate = new("SpaceSimulation.OnFixedUpdate");
        static readonly ProfilerMarker VesselBehavior_OnFixedUpdate = new("VesselBehavior.OnFixedUpdate");

        // PartBehavior
        [HarmonyPrefix]
        [HarmonyPatch(typeof(PartBehavior), nameof(PartBehavior.OnFixedUpdate))]
        public static void PartBehavior_OnFixedUpdate_MarkerBegin(PartBehavior __instance)
            => PartBehaviour_OnFixedUpdate.Begin(__instance);

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PartBehavior), nameof(PartBehavior.OnFixedUpdate))]
        public static void PartBehavior_OnFixedUpdate_MarkerEnd()
            => PartBehaviour_OnFixedUpdate.End();

        // PartBehaviourModule
        [HarmonyPrefix]
        [HarmonyPatch(typeof(PartBehaviourModule), nameof(PartBehaviourModule.OnFixedUpdate))]
        public static void PartBehaviourModule_OnFixedUpdate_MarkerBegin(PartBehaviourModule __instance)
            => PartBehaviourModule_OnFixedUpdate.Begin(__instance);

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PartBehaviourModule), nameof(PartBehaviourModule.OnFixedUpdate))]
        public static void PartBehaviourModule_OnFixedUpdate_MarkerEnd()
            => PartBehaviourModule_OnFixedUpdate.End();

        // PhysicsSpaceProvider
        [HarmonyPrefix]
        [HarmonyPatch(typeof(PhysicsSpaceProvider), "IFixedUpdate.OnFixedUpdate")]
        public static void PhysicsSpaceProvider_OnFixedUpdate_MarkerBegin(PhysicsSpaceProvider __instance)
            => PhysicsSpaceProvider_OnFixedUpdate.Begin(__instance);

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PhysicsSpaceProvider), "IFixedUpdate.OnFixedUpdate")]
        public static void PhysicsSpaceProvider_OnFixedUpdate_MarkerEnd()
            => PhysicsSpaceProvider_OnFixedUpdate.End();

        // RigidbodyBehavior
        [HarmonyPrefix]
        [HarmonyPatch(typeof(RigidbodyBehavior), nameof(RigidbodyBehavior.OnFixedUpdate))]
        public static void RigidbodyBehavior_OnFixedUpdate_MarkerBegin(RigidbodyBehavior __instance)
            => RigidbodyBehavior_OnFixedUpdate.Begin(__instance);

        [HarmonyPostfix]
        [HarmonyPatch(typeof(RigidbodyBehavior), nameof(RigidbodyBehavior.OnFixedUpdate))]
        public static void RigidbodyBehavior_OnFixedUpdate_MarkerEnd()
            => RigidbodyBehavior_OnFixedUpdate.End();

        // SpaceSimulation
        [HarmonyPrefix]
        [HarmonyPatch(typeof(SpaceSimulation), nameof(SpaceSimulation.OnFixedUpdate), new System.Type[] { typeof(float), typeof(bool) })]
        public static void SpaceSimulation_OnFixedUpdate_MarkerBegin()
            => SpaceSimulation_OnFixedUpdate.Begin();

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SpaceSimulation), nameof(SpaceSimulation.OnFixedUpdate), new System.Type[] { typeof(float), typeof(bool) })]
        public static void SpaceSimulation_OnFixedUpdate_MarkerEnd()
            => SpaceSimulation_OnFixedUpdate.End();

        // VesselBehavior
        [HarmonyPrefix]
        [HarmonyPatch(typeof(VesselBehavior), nameof(VesselBehavior.OnFixedUpdate))]
        public static void VesselBehavior_OnFixedUpdate_MarkerBegin(RigidbodyBehavior __instance)
            => VesselBehavior_OnFixedUpdate.Begin(__instance);

        [HarmonyPostfix]
        [HarmonyPatch(typeof(VesselBehavior), nameof(VesselBehavior.OnFixedUpdate))]
        public static void VesselBehavior_OnFixedUpdate_MarkerEnd()
            => VesselBehavior_OnFixedUpdate.End();
    }
}
