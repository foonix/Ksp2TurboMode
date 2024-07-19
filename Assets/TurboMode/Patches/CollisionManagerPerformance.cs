using KSP.Sim;
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
    // === Justification ===
    // CollisionManager.UpdatePartCollisionIgnores() is O(n^2), scaling with collider count.
    // That is called each frame after adding ~10 or so parts, resulting in approximately O(!(n^2)) on large craft.
    // For high part count vessels, loading frame rate bogs down, and any dock/undock/breakoff operation results in frame drop.

    // === Assumptions ===
    // PhysicsSettings.ENABLE_PART_TO_PART_COLLISIONS is never true.
    // That setting enables parts on the same vehicle to collide with each other unless the collider has the
    // NoSameVesselCollision tag.  As far as I know, this isn't used.

    // === Optimizations ===
    // Simplify by dropping support for part-to-part collisions, ignore the related tag, and don't track parts individually.
    // Avoid Transform.Find*() operations.  Use available data such as PartBehavior.Colliders instead.
    // Use sets to push wort-case closer to O(n * log(m)), minimising calls to Physics.IgnoreCollision().

    public class CollisionManagerPerformance
    {
        static readonly Dictionary<CollisionManager, VesselData> vesselData = new();

        static readonly ProfilerMarker CollisionManagerPerformance_GetOrCreateVesselData = new("CollisionManagerPerformance.GetOrCreateVesselData");

        public static List<IDetour> MakeHooks() => new()
        {
            //new ILHook(typeof(PartBehavior).GetMethod("Start",BindingFlags.NonPublic |BindingFlags.Instance), PartBehavior_Start_Patch),
            new Hook(
                typeof(CollisionManager).GetMethod("OnCollisionIgnoreUpdate"),
                (Action<Action<CollisionManager>, CollisionManager>)LogMissedCall
                ),
            //new Hook(
            //    typeof(CollisionManager).GetMethod("UpdatePartCollisionIgnores", BindingFlags.Instance | BindingFlags.NonPublic),
            //    (Action<Action<CollisionManager>, CollisionManager>)MissingColliderCheck
            //    ),
        };

        public static void LogMissedCall(Action<CollisionManager> orig, CollisionManager cm)
        {
            if (!TurboModePlugin.testMode) { return; }
            Debug.LogError($"Process called to OnCollisionIgnoreUpdate for {cm.name}");
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

            var data = GetOrCreateVesselData(cm);
            Debug.Log($"Starting CM check for {data.vessel}");

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
                    if (!data.vcsModel.colliders.Contains(cmCollider))
                    {
                        Debug.Log($"Missing collider {cmCollider.name}");
                        continue;
                    }

                    // check assumptions about physics
                    foreach (var otherTrackedCollider in data.vcsModel.colliders)
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

        static void PartBehavior_Start_Patch(ILContext context)
        {
            ILCursor cursor = new(context);
            cursor.GotoNext(inst => inst.MatchCallvirt(typeof(CollisionManager), "OnCollisionIgnoreUpdate"));
            cursor.Remove();  // we are replacing this call
            // CollisionManager is already on the stack
            cursor
                .Emit(OpCodes.Ldarg_0) // this (PartBehavior)
                .Emit(OpCodes.Callvirt, typeof(CollisionManagerPerformance).GetMethod("AddPartAdditive"));
        }

        private struct VesselData
        {
            public VesselSelfCollide vcsModel;
            public VesselBehavior vessel;

            public VesselData(VesselBehavior vessel)
            {
                this.vessel = vessel;
                vcsModel = new(vessel.SimObjectComponent);
            }
        }



        private static VesselData GetOrCreateVesselData(CollisionManager cm)
        {
            CollisionManagerPerformance_GetOrCreateVesselData.Begin(cm);
            if (vesselData.TryGetValue(cm, out VesselData data))
            {
                CollisionManagerPerformance_GetOrCreateVesselData.End();
                return data;
            }

            data = new VesselData(cm.Vessel);
            vesselData.Add(cm, data);
            //cm.Vessel.PartOwner.SimObjectComponent.PartsAdded += (parts) =>
            //{
            //    data.vcsModel.OnPartsChangedVessel(cm, parts, true);
            //};
            //cm.Vessel.PartOwner.SimObjectComponent.PartsRemoved += (parts) =>
            //{
            //    data.vcsModel.OnPartsChangedVessel(cm, parts, false);
            //};

            CollisionManagerPerformance_GetOrCreateVesselData.End();
            return data;
        }


    }
}
