using KSP.Game;
using KSP.Sim.impl;
using MonoMod.RuntimeDetour;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using VehiclePhysics;

namespace TurboMode
{
    // It may be better to turn off autosync entirely and just sync during specific phases of the frame.
    // But to hedge my bets against obscure bugs, this code just turns it off in specific areas
    // that I can reasonably identify as unlikely to be problematic and acchieve a measurable performance gain.
    public class SelectivePhysicsAutoSync
    {
        public static List<IDetour> MakeHooks() => new()
        {
            // RigidbodyBehavior drive a large number of transform changes during GameInstance.Update().
            new Hook(
                typeof(RigidbodyBehavior).GetMethod("OnUpdate"),
                (Action<Action<System.Object, float>, RigidbodyBehavior, float>)SeparatePartsFromOther
                ),

            // write-only, VFX transforms I think.
            new Hook(
                typeof(VesselBehavior).GetMethod("OnUpdate"),
                (Action<Action<System.Object, float>, System.Object, float>)SyncAfter
                ),

            // SpaceSimulation updates are unordered, so things like update interdepended
            // moves and raycast queries are a bad idea here anyway.
            // But we do need to have current data because some objects do raycast against the
            // Rigidbody/floating origin changes that happen just before this.
            new Hook(
                typeof(SpaceSimulation).GetMethod("IUpdate.OnUpdate", BindingFlags.Instance | BindingFlags.NonPublic),
                (Action<Action<System.Object, float>, System.Object, float>)BookendSyncs
                ),

            // UIManager seems to only do racast queries here.
            // Cut out the overhead in checking for updates triggered by Collider.Raycast().
            new Hook(
                typeof(UIManager).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic),
                (Action<Action<System.Object>, System.Object>)RunWithoutAutosync
                ),

            // Just reads the positions of colliders.
            new Hook(
                typeof(InteractSystem).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic),
                (Action<Action<System.Object>, System.Object>)BookendSyncs
                ),

            // These update the visual position of wheels.
            // They do intermix raycasting and transform changes, however the raycast
            // is primarily concerned with keeping the wheel above the service it is driving over.
            // The worst case I can imagine is a moster truck driving over a pile of wheel objects, the position may not 
            // reflect the changes driven by eachother within the same frame.
            // But they're not calculated in any ordered way anyway, so they already wouldin't be perfectly accurate.
            new Hook(
                typeof(VehicleBase).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic),
                (Action<Action<System.Object>, System.Object>)RunWithoutAutosync
                ),
        };

        private static void SeparatePartsFromOther(Action<System.Object, float> orig, RigidbodyBehavior instance, float deltaTime)
        {
            RunWithoutAutosync(orig, instance, deltaTime);

            // Syncing vessels here seems to fix an obscure issue
            // with parts colliding into the ocean when well above sealevel.
            // Parts have priority 4, everything else is priority 3.
            if (instance.ExecutionPriorityOverride == 3 && instance.ViewObject.Vessel != null)
            {
                Physics.SyncTransforms();
            }
        }

        private static void SyncAfter(Action<System.Object, float> orig, System.Object instance, float deltaTime)
        {
            RunWithoutAutosync(orig, instance, deltaTime);
            Physics.SyncTransforms();
        }

        private static void BookendSyncs(Action<System.Object, float> orig, System.Object instance, float deltaTime)
        {
            Physics.SyncTransforms();
            RunWithoutAutosync(orig, instance, deltaTime);
            Physics.SyncTransforms();
        }

        private static void BookendSyncs(Action<System.Object> orig, System.Object instance)
        {
            Physics.SyncTransforms();
            RunWithoutAutosync(orig, instance);
            Physics.SyncTransforms();
        }

        private static void RunWithoutAutosync(Action<System.Object> orig, System.Object instance)
        {
            if (!TurboModePlugin.testModeEnabled)
            {
                Physics.autoSyncTransforms = false;
            }
            orig(instance);
            if (!TurboModePlugin.testModeEnabled)
            {
                Physics.autoSyncTransforms = true;
            }
        }

        private static void RunWithoutAutosync(Action<System.Object, float> orig, System.Object instance, float deltaTime)
        {
            if (!TurboModePlugin.testModeEnabled)
            {
                Physics.autoSyncTransforms = false;
            }
            orig(instance, deltaTime);
            if (!TurboModePlugin.testModeEnabled)
            {
                Physics.autoSyncTransforms = true;
            }
        }

    }
}