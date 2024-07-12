using KSP.Sim.Definitions;
using KSP.Sim.impl;
using MonoMod.RuntimeDetour;
using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Profiling;

namespace TurboMode
{
    public class AdditionalProfilerTags
    {
        private readonly ProfilerMarker marker;

        private delegate void fixedUpdateSig(Action<UnityEngine.Object, float> orig, UnityEngine.Object contextObj, float deltaTime);
        private delegate void fixedUpdateSigNoContext(Action<System.Object, float> orig, System.Object contextObj, float deltaTime);

        private AdditionalProfilerTags(ProfilerMarker marker)
        {
            this.marker = marker;
        }

        public static List<IDetour> MakeHooks() => new()
            {
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

        private Hook MakeHook(MethodInfo source) => new(source, (fixedUpdateSig)WrapFixedUpdate);

        private Hook MakeHookNoContext(MethodInfo source) => new(source, (fixedUpdateSigNoContext)WrapFixedUpdateNoContext);
    }
}
