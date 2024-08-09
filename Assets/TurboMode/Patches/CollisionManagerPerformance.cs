using KSP.Game;
using KSP.Messages;
using KSP.Sim.impl;
using MonoMod.RuntimeDetour;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TurboMode.Models;
using UnityEngine;

namespace TurboMode
{
    public static class CollisionManagerPerformance
    {
        public static List<IDetour> MakeHooks() => new()
        {
            new Hook(
                typeof(CollisionManager).GetMethod("OnCollisionIgnoreUpdate"),
                (Action<Action<CollisionManager>, CollisionManager>)LogMissedCall),
            new Hook(
                typeof(CollisionManager).GetMethod("UpdatePartCollisionIgnores", BindingFlags.Instance | BindingFlags.NonPublic),
                (Action<Action<CollisionManager>, CollisionManager>)MissingColliderCheck),
            new Hook(
                typeof(SpaceSimulation).GetMethod("CreateCombinedVesselSimObject"),
                (Func<
                    Func<SpaceSimulation, VesselComponent, PartComponent, VesselComponent, PartComponent, SimulationObjectModel>,
                    SpaceSimulation, VesselComponent, PartComponent, VesselComponent, PartComponent,
                    SimulationObjectModel
                    >)MergeCombinedVessels),
        };

        public static void OnGameInstanceInitialized(GameInstance gameInstance)
        {

#if TURBOMODE_TRACE_EVENTS
            // This is not called when a vessel is loaded in some cases, eg loading from saved game.
            gameInstance.Messages.Subscribe<VesselCreatedMessage>((message) =>
            {
                var createdMessage = message as VesselCreatedMessage;
                Debug.Log($"TM: Vessel created message {createdMessage.vehicle} {createdMessage.SerializedVessel} {createdMessage.serializedLocation} ({Time.frameCount})");
            });
#endif

            gameInstance.Messages.Subscribe<PartBehaviourInitializedMessage>((message) =>
            {
                var msg = message as PartBehaviourInitializedMessage;
                var part = msg.Part;
#if TURBOMODE_TRACE_EVENTS
                Debug.Log($"TM: Part initialized message {part.name} ({Time.frameCount})");
#endif

                // The game will "smear" part add overhead across multiple frames before calling
                // VesselBehaviorInitializedMessage.  So smear collider ignore in the same way.
                var vesselSimObj = part.vessel.SimObjectComponent.SimulationObject;
                if (!vesselSimObj.TryFindComponent<VesselSelfCollide>(out VesselSelfCollide vsc))
                {
                    vesselSimObj.AddComponent(
                    vsc = new VesselSelfCollide(part.vessel.SimObjectComponent),
                        0 // fixme
                        );
                }
                vsc.AddPartAdditive(part);
            });

            // This is one of the last calls after a vessel is loaded or split from another vessel.
            // It doesn't really specify why the vessel was initialized, so its usefulness is limited.
            gameInstance.Messages.Subscribe<VesselBehaviorInitializedMessage>((message) =>
            {
                var msg = message as VesselBehaviorInitializedMessage;
#if TURBOMODE_TRACE_EVENTS
                Debug.Log($"TM: Vessel initialized message {msg.NewVesselBehavior.name} ({Time.frameCount})");
#endif

                var simObj = msg.NewVesselBehavior.SimObjectComponent.SimulationObject;
                if (!simObj.TryFindComponent<VesselSelfCollide>(out var vsc))
                {
                    msg.NewVesselBehavior.SimObjectComponent.SimulationObject.AddComponent(
                        vsc = new VesselSelfCollide(msg.NewVesselBehavior.SimObjectComponent),
                        0 // fixme
                        );
                    // I don't know of a case yet where VesselBehaviorInitializedMessage
                    // is called on a vessel that isn't already had one of the other messages I'm handling,
                    // so I'm assuming this is a broken off part (or tree thereof) that got promoted to a new vessel.
                    // In that case just track the colliders because they already ignore eachother.
                    // That way if they split again they can be properly split using the PartOwner events.
                    vsc.TrackPartsAfterSplit();
                }
            });

            // This message is broken.  It returns the newly created "merged" vessel sim object
            // as both VesselOne and VesselTwo.
            // It may be better to hook into
            // SpaceSimulation.CreateCombinedVesselSimObject()
            // to get all 3 (original, new master, and added vessel)
            gameInstance.Messages.Subscribe<VesselDockedMessage>((message) =>
            {
                var msg = message as VesselDockedMessage;
#if TURBOMODE_TRACE_EVENTS
                Debug.Log($"TM: Vessel docked message {msg.VesselOne} {msg.VesselTwo} ({Time.frameCount})");
#endif
                var simObj = msg.VesselOne.SimObjectComponent.SimulationObject;
                if (!simObj.TryFindComponent<VesselSelfCollide>(out var vsc))
                {
                    simObj.AddComponent(
                        vsc = new VesselSelfCollide(msg.VesselOne.SimObjectComponent),
                        0 // fixme
                        );
                    // last resort to ensure things are correct.
                    vsc.FindNewColliders();
                }
            });

            // After undock or staging, remainingVessel was the original vessel,
            // and newVessel is the part that fell off.
            // remainingVessel will have gotten PartOwnerComponent.PartsRemoved
            // events already (enabling the physics collisions between vessels),
            // so we only need to make newVessel track colliders it has, which are already ignoring each other.
            gameInstance.Messages.Subscribe<VesselSplitMessage>((message) =>
            {
                var msg = message as VesselSplitMessage;
#if TURBOMODE_TRACE_EVENTS
                Debug.Log($"TM: Vessel split message {msg.remainingVessel.Name} {msg.newVessel.Name} {msg.isNewVesselFromSubVessel} ({Time.frameCount})");
#endif
                var simObj = msg.newVessel.SimulationObject;
                if (!simObj.TryFindComponent<VesselSelfCollide>(out var vsc))
                {
                    simObj.AddComponent(
                        vsc = new VesselSelfCollide(msg.newVessel),
                        0 // fixme
                        );

                    vsc.TrackPartsAfterSplit();
                }
            });

#if TURBOMODE_TRACE_EVENTS
            gameInstance.Messages.Subscribe<PartDetachedMessage>((message) =>
            {
                var msg = message as PartDetachedMessage;
                Debug.Log($"TM: Part detached {msg.PartBehavior} ({Time.frameCount})");
            });
            gameInstance.Messages.Subscribe<PartJointBroken>((message) =>
            {
                var msg = message as PartJointBroken;
                Debug.Log($"TM: Joint broken {msg.PartBehavior} {msg.OtherPartBehavior} ({Time.frameCount})");
            });
            gameInstance.Messages.Subscribe<PartDestroyedMessage>((message) =>
            {
                var msg = message as PartDestroyedMessage;
                Debug.Log($"TM: Part destroyed {msg.PartBehavior} ({Time.frameCount})");
            });
            gameInstance.Messages.Subscribe<PartCrashedMessage>((message) =>
            {
                var msg = message as PartCrashedMessage;
                Debug.Log($"TM: Part crashed {msg.PartBehavior} ({Time.frameCount})");
            });
#endif
        }

        public static void LogMissedCall(Action<CollisionManager> orig, CollisionManager cm)
        {
#if TURBOMODE_TRACE_EVENTS
            //Debug.LogError($"Process called to OnCollisionIgnoreUpdate for {cm.name}");
#endif

            // the goal here is to obviate this, but leaving it on to check
            // if my code is not processing colliders it should be.
            orig(cm);
        }

        public static void MissingColliderCheck(Action<CollisionManager> orig, CollisionManager cm)
        {
            // Plug the leaks to make the mode more usable, until I can figure out everything that
            // spontaneously sprouts and destroys colliders. It's still decently fast compared to CollisionManager.
            if (cm.Vessel.SimObjectComponent.SimulationObject.TryFindComponent(out VesselSelfCollide vsc) && !TurboModePlugin.testModeEnabled)
            {
                vsc.FindNewColliders();
            }

            if (!TurboModePlugin.testModeEnabled)
            {
                return;
            }

            // let CollisionManager do its thing
            orig(cm);

            var field = typeof(CollisionManager).GetField("_vesselPartsList", BindingFlags.Instance | BindingFlags.NonPublic);
            IEnumerable partsLists = field.GetValue(cm) as IEnumerable;

            if (vsc is null)
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

        private static SimulationObjectModel MergeCombinedVessels(
            Func<SpaceSimulation, VesselComponent, PartComponent, VesselComponent, PartComponent, SimulationObjectModel> orig,
            SpaceSimulation sim,
            VesselComponent masterVessel, PartComponent masterAttachPart,
            VesselComponent otherVessel, PartComponent otherAttachPart)
        {
            Debug.Log($"TM: Merging colliders for {masterVessel} {otherVessel}");

            var masterColliders = masterVessel.SimulationObject.FindComponent<VesselSelfCollide>().colliders;
            var otherColliders = otherVessel.SimulationObject.FindComponent<VesselSelfCollide>().colliders;

            var newSimObj = orig(sim, masterVessel, masterAttachPart, otherVessel, otherAttachPart);

            var vsc = new VesselSelfCollide(newSimObj.Vessel);
            newSimObj.AddComponent(vsc, 0f);

            vsc.MergeCombinedVesselColliders(masterColliders, otherColliders);

            return newSimObj;
        }
    }
}
