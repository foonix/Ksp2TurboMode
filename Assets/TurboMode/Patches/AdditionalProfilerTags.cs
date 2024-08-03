using KSP.Sim.Definitions;
using KSP.Sim.impl;
using KSP.Sim.ResourceSystem;
using MonoMod.RuntimeDetour;
using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Profiling;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TurboMode
{
    public class AdditionalProfilerTags
    {
        private readonly ProfilerMarker marker;
        private static readonly ProfilerMarker graphicRaycasterMarker = new("GraphicRaycaster.Raycast()");

        private delegate void fixedUpdateSig(Action<UnityEngine.Object, float> orig, UnityEngine.Object contextObj, float deltaTime);
        private delegate void fixedUpdateSigNoContext(Action<System.Object, float> orig, System.Object contextObj, float deltaTime);
        private delegate void spaceSimFixedUpdateSigNoContext(Action<System.Object, double, double> orig, System.Object contextObj, double universalTime, double deltaUniversalTime);
        private delegate void voidMethod(Action<System.Object> orig, System.Object contextObj);

        private AdditionalProfilerTags(ProfilerMarker marker)
        {
            this.marker = marker;
        }

        public static List<IDetour> MakeHooks() => new()
            {
                // GameInstance Update()
                new AdditionalProfilerTags(new("SpaceSimulation.OnUpdate"))
                    .MakeHookNoContext(typeof(SpaceSimulation).GetMethod("IUpdate.OnUpdate", BindingFlags.Instance | BindingFlags.NonPublic)),
                new AdditionalProfilerTags(new("VesselBehavior.OnUpdate"))
                    .MakeHook(typeof(VesselBehavior).GetMethod("OnUpdate")),
                new AdditionalProfilerTags(new("PartBehavior.OnUpdate"))
                    .MakeHook(typeof(PartBehavior).GetMethod("IUpdate.OnUpdate", BindingFlags.Instance | BindingFlags.NonPublic)),
                new AdditionalProfilerTags(new("RigidbodyBehavior.OnUpdate"))
                    .MakeHook(typeof(RigidbodyBehavior).GetMethod("OnUpdate")),

                // GameInstance FixedUpdate
                new AdditionalProfilerTags(new("PartBehaviour.OnFixedUpdate"))
                    .MakeHook(typeof(PartBehavior).GetMethod("OnFixedUpdate")),
                new AdditionalProfilerTags(new("PartBehaviourModule.OnFixedUpdate"))
                    .MakeHook(typeof(PartBehaviourModule).GetMethod("OnFixedUpdate")),
                new AdditionalProfilerTags(new("PhysicsSpaceProvider.OnFixedUpdate"))
                    .MakeHook(typeof(PhysicsSpaceProvider).GetMethod("IFixedUpdate.OnFixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic)),
                new AdditionalProfilerTags(new("RigidbodyBehavior.OnFixedUpdate"))
                    .MakeHook(typeof(RigidbodyBehavior).GetMethod("OnFixedUpdate")),
                new AdditionalProfilerTags(new("SpaceSimulation.OnFixedUpdate"))
                    .MakeHookNoContext(typeof(SpaceSimulation).GetMethod("OnFixedUpdate", new Type[] { typeof(float) })),
                new AdditionalProfilerTags(new("VesselBehavior.OnFixedUpdate"))
                    .MakeHook(typeof(VesselBehavior).GetMethod("OnFixedUpdate")),

                // SpaceSimulation.OnFixedUpdate component updates
                new AdditionalProfilerTags(new("PartComponent.OnFixedUpdate"))
                    .MakeSpaceSimulationFixedUpdate(typeof(PartComponent).GetMethod("OnFixedUpdate")),
                new AdditionalProfilerTags(new("CelestialBodyComponent.OnFixedUpdate"))
                    .MakeSpaceSimulationFixedUpdate(typeof(CelestialBodyComponent).GetMethod("OnFixedUpdate")),
                new AdditionalProfilerTags(new("OrbiterComponent.OnFixedUpdate"))
                    .MakeSpaceSimulationFixedUpdate(typeof(OrbiterComponent).GetMethod("OnFixedUpdate")),
                new AdditionalProfilerTags(new("PartOwnerComponent.OnFixedUpdate"))
                    .MakeSpaceSimulationFixedUpdate(typeof(PartOwnerComponent).GetMethod("OnFixedUpdate")),
                new AdditionalProfilerTags(new("RigidbodyComponent.OnFixedUpdate"))
                    .MakeSpaceSimulationFixedUpdate(typeof(RigidbodyComponent).GetMethod("OnFixedUpdate")),
                new AdditionalProfilerTags(new("VesselComponent.OnFixedUpdate"))
                    .MakeSpaceSimulationFixedUpdate(typeof(VesselComponent).GetMethod("OnFixedUpdate")),

                // PartOwnerComponent FixedUpdate sub-functions
                new AdditionalProfilerTags(new("PartOwnerComponent.UpdateMassStats"))
                    .MakeWrapVoid(typeof(PartOwnerComponent).GetMethod("UpdateMassStats")),
                new AdditionalProfilerTags(new("PartOwnerComponent.CalculatePhysicsStats"))
                    .MakeWrapVoid(typeof(PartOwnerComponent).GetMethod("CalculatePhysicsStats")),
                new AdditionalProfilerTags(new("ResourceFlowRequestManager.UpdateFlowRequests"))
                    .MakeSpaceSimulationFixedUpdate(typeof(ResourceFlowRequestManager).GetMethod("UpdateFlowRequests")),

                // Trying to figure out why EventSystem takes an entire ms
                new Hook(
                    typeof(GraphicRaycaster).GetMethod("Raycast"),
                    (Action<Action<GraphicRaycaster, PointerEventData, List<RaycastResult>>, GraphicRaycaster, PointerEventData, List<RaycastResult>>)WrapGraphicRaycaster
                ),
            };

        private void WrapFixedUpdate(Action<UnityEngine.Object, float> orig, UnityEngine.Object contextObj, float deltaTime)
        {
            marker.Begin(contextObj);
            orig(contextObj, deltaTime);
            marker.End();
        }

        private void WrapFixedUpdateNoContext(Action<System.Object, float> orig, System.Object contextObj, float deltaTime)
        {
            marker.Begin();
            orig(contextObj, deltaTime);
            marker.End();
        }

        private void WrapVoidMethod(Action<System.Object> orig, System.Object contextObj)
        {
            marker.Begin();
            orig(contextObj);
            marker.End();
        }

        private void WrapSpaceSimulationFixedUpdate(
            Action<System.Object, double, double> orig,
            System.Object contextObj,
            double universalTime,
            double deltaUniversalTime)
        {
            marker.Begin();
            orig(contextObj, universalTime, deltaUniversalTime);
            marker.End();
        }

        private static void WrapGraphicRaycaster(
            Action<GraphicRaycaster, PointerEventData, List<RaycastResult>> orig,
            GraphicRaycaster gr,
            PointerEventData pointerEventData,
            List<RaycastResult> raycastResults)
        {
            graphicRaycasterMarker.Begin(gr);
            orig(gr, pointerEventData, raycastResults);
            graphicRaycasterMarker.End();
        }

        private Hook MakeHook(MethodInfo source) => new(source, (fixedUpdateSig)WrapFixedUpdate);

        private Hook MakeHookNoContext(MethodInfo source) => new(source, (fixedUpdateSigNoContext)WrapFixedUpdateNoContext);

        private Hook MakeSpaceSimulationFixedUpdate(MethodInfo source)
            => new(source, (spaceSimFixedUpdateSigNoContext)WrapSpaceSimulationFixedUpdate);

        private Hook MakeWrapVoid(MethodInfo source)
            => new(source, (voidMethod)WrapVoidMethod);
    }
}
