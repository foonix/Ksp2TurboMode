using KSP.Game;
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
            // Things like raycast queries are a bad idea here anyway, because the IUpdate interface
            // does not guarantee ordering.  
            new Hook(
                typeof(GameInstance).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic),
                (Action<Action<System.Object>, System.Object>)DisablePhysicsAutoSyncThenSync
                ),

            // UIManager seems to only do racast queries here.
            // Cut out the overhead in checking for updates triggered by Collider.Raycast().
            new Hook(
                typeof(UIManager).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic),
                (Action<Action<System.Object>, System.Object>)DisablePhysicsAutoSync
                ),

            // These update the visual position of wheels.
            // They do intermix raycasting and transform changes, however the raycast
            // is primarily concerned with keeping the wheel above the service it is driving over.
            // The worst case I can imagine is a moster truck driving over a pile of wheel objects, the position may not 
            // reflect the changes driven by eachother within the same frame.
            // But they're not calculated in any ordered way anyway, so they already wouldin't be perfectly accurate.
            new Hook(
                typeof(VehicleBase).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic),
                (Action<Action<System.Object>, System.Object>)DisablePhysicsAutoSync
                ),
        };

        // Batching changes to large numbers of transforms.
        private static void DisablePhysicsAutoSyncThenSync(Action<System.Object> orig, System.Object instance)
        {
            RunWithoutAutosync(orig, instance);
            Physics.SyncTransforms();
        }

        // This is faster for processes that only do read queries.
        private static void DisablePhysicsAutoSync(Action<System.Object> orig, System.Object instance)
        {
            RunWithoutAutosync(orig, instance);
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
    }
}