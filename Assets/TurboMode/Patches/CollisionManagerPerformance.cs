using KSP.Sim.impl;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TurboMode.Models;
using Unity.Profiling;
using UnityEngine;

namespace TurboMode
{
    public static class CollisionManagerPerformance
    {
        public static List<IDetour> MakeHooks() => new()
        {
            new Hook(
                typeof(CollisionManager).GetMethod("OnCollisionIgnoreUpdate"),
                (Action<Action<CollisionManager>, CollisionManager>)LogMissedCall
                ),
            new Hook(
                typeof(CollisionManager).GetMethod("UpdatePartCollisionIgnores", BindingFlags.Instance | BindingFlags.NonPublic),
                (Action<Action<CollisionManager>, CollisionManager>)MissingColliderCheck
                ),
        };

        public static void LogMissedCall(Action<CollisionManager> orig, CollisionManager cm)
        {
            if (!TurboModePlugin.testModeEnabled) { return; }
#if TURBOMODE_TRACE_EVENTS
            Debug.LogError($"Process called to OnCollisionIgnoreUpdate for {cm.name}");
#endif
            // the goal here is to obviate this, but leaving it on to check
            // if my code is not processing colliders it should be.
            orig(cm);
        }

        public static void MissingColliderCheck(Action<CollisionManager> orig, CollisionManager cm)
        {
            // let CollisionManager do its thing
            orig(cm);

            var field = typeof(CollisionManager).GetField("_vesselPartsList", BindingFlags.Instance | BindingFlags.NonPublic);
            IEnumerable partsLists = field.GetValue(cm) as IEnumerable;

            if (!cm.Vessel.SimObjectComponent.SimulationObject.TryFindComponent(out VesselSelfCollide vsc))
            {
                Debug.Log($"TM: Vessel {cm.Vessel} is missing VesselSelfCollide component!");
                return;
            }
            Debug.Log($"Starting CM check for {cm.Vessel}");

            foreach (var partsList in partsLists)
            {
                var colliders = partsList.GetType()
                    .GetField("Colliders")
                    .GetValue(partsList) as List<Collider>;

                foreach (var cmCollider in colliders)
                {
                    var parentPart = cmCollider.GetComponentInParent<PartBehavior>();

                    if (!parentPart.Colliders.Contains(cmCollider))
                    {
                        Debug.Log($"Part {parentPart} collider list doesn't contain {cmCollider}");
                    }

                    if (!(cmCollider.gameObject.activeInHierarchy && cmCollider.enabled))
                    {
                        continue;
                    }

                    // check that I'm tracking all of the colliders I'm supposed to
                    if (!vsc.colliders.Contains(cmCollider))
                    {
                        Debug.Log($"Missing collider {cmCollider.name}");
                        continue;
                    }

                    // check assumptions about physics
                    foreach (var otherTrackedCollider in vsc.colliders)
                    {
                        // skip colliders we are keeping track of colliders that CM has cleaned up because they're not enabled
                        // RCS thrusters toggle their GameObject enabled for the sfx for each port.
                        // Some things specifically toggle the collider.
                        if (cmCollider == otherTrackedCollider
                            || !otherTrackedCollider || !otherTrackedCollider.enabled || !otherTrackedCollider.gameObject.activeInHierarchy)
                        {
                            continue;
                        }

                        bool isIgnored = Physics.GetIgnoreCollision(cmCollider, otherTrackedCollider);
                        bool notSameRigidbody = cmCollider.attachedRigidbody != otherTrackedCollider.attachedRigidbody;
                        bool myAssumption = notSameRigidbody;
                        if (isIgnored != myAssumption)
                        {
                            Debug.Log($"Assumption check failed: {isIgnored} {cmCollider.name}({cmCollider.isTrigger}) {otherTrackedCollider.name}({otherTrackedCollider.isTrigger})");
                        }
                    }
                }
            }
        }
    }
}
