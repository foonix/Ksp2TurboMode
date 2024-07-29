using KSP.Game;
using KSP.Modules;
using KSP.Sim;
using KSP.Sim.Converters;
using KSP.Sim.impl;
using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace TurboMode.Models
{
    // === Justification ===
    // CollisionManager.UpdatePartCollisionIgnores() is O(n^2), scaling with collider count.
    // That is called each frame after adding ~10 or so parts, resulting in approximately O(!(n^2)) on large craft.
    // For high part count vessels, loading frame rate bogs down, and any dock/undock/breakoff operation results in frame drop.

    // === Assumptions ===
    // PhysicsSettings.ENABLE_PART_TO_PART_COLLISIONS is never true.
    // That setting enables parts on the same vehicle to collide with each other unless the collider has the
    // NoSameVesselCollision tag.  As far as I know, this isn't used.
    // Matching the exact behavior of when the game makes colliders ignore each other is problematic due to
    // Rube Goldberg setup process of creating the collider and then later creating Rigidbody.

    // === Optimizations ===
    // Simplify by dropping support for part-to-part collisions, ignore the related tag, and don't track parts individually.
    // Make every part ignore every other part so that we don't have to redo things if Rigidbody changes.
    // Avoid Transform.Find*() operations.  Use available data such as PartBehavior.Colliders instead.
    // Use sets to push wort-case closer to O(n * log(m)), minimising calls to Physics.IgnoreCollision().
    public class VesselSelfCollide : ObjectComponent
    {
        [TypeConverterIgnore]
        public readonly HashSet<Collider> colliders = new(100);

        private readonly VesselComponent vessel;
        private readonly List<Collider> _tempColliderStorage = new(100);

        static readonly ProfilerMarker VesselSelfCollide_AddCollider = new("VesselSelfCollide.AddCollider");
        static readonly ProfilerMarker VesselSelfCollide_AddPartAdditive = new("VesselSelfCollide.AddPartAdditive");
        static readonly ProfilerMarker VesselSelfCollide_OnPartsChangedVessel = new("VesselSelfCollide.OnPartsChangedVessel");
        static readonly ProfilerMarker VesselSelfCollide_TrackAfterSplit = new("VesselSelfCollide.TrackAfterSplit");
        static readonly ProfilerMarker VesselSelfCollide_FindNewColliders = new("VesselSelfCollide.FindNewColliders");
        static readonly ProfilerMarker VesselSelfCollide_MergeCombinedVesselColliders = new("VesselSelfCollide.MergeCombinedVesselColliders");

        public VesselSelfCollide(VesselComponent vessel)
        {
            this.vessel = vessel;
        }

        public override void OnStart(double universalTime)
        {
            base.OnStart(universalTime);
            if (SimulationObject.PartOwner is null)
            {
                SimulationObject.onComponentAdded += WaitForPartOwner;
            }
            else
            {
                SimulationObject.PartOwner.PartsRemoved += OnPartsRemoved;
            }
        }

        private void WaitForPartOwner(Type type, ObjectComponent component)
        {
            if (component is PartOwnerComponent po)
            {
                po.PartsRemoved += OnPartsRemoved;
                SimulationObject.onComponentAdded -= WaitForPartOwner;
            }
        }

        private void OnPartsRemoved(List<PartComponent> list)
        {
            OnPartsChangedVessel(list, false);
        }

        public void OnPartsChangedVessel(List<PartComponent> parts, bool adding)
        {
            VesselSelfCollide_OnPartsChangedVessel.Begin(SimulationObject.objVesselBehavior);
#if TURBOMODE_TRACE_EVENTS
            Debug.Log(string.Format("TM: {0} {1} parts for {2}",
                adding ? "Adding" : "Removing",
                parts.Count,
                vessel.Name));
#endif

            // not separating parts from each other on the assumption that each group
            // removed is going to the same vessel or debris
            foreach (var partComponent in parts)
            {
                var part = GetPartBehavior(partComponent);
                if (!part) continue;

                // even if the collider is already destroyed, try to remove the reference from the set.
                foreach (var removingCollider in part.Colliders)
                {
                    bool canged = adding ? colliders.Add(removingCollider) : colliders.Remove(removingCollider);
                    if (canged)
                    {
                        _tempColliderStorage.Add(removingCollider);
                    }
                }
            }

            List<Collider> pruneDestroyed = null;
            foreach (var remainingCollider in colliders)
            {
                if (!remainingCollider)
                {
                    pruneDestroyed ??= new(4);
                    pruneDestroyed.Add(remainingCollider);
                    continue;
                }

                foreach (var removedCollider in _tempColliderStorage)
                {
                    if (removedCollider && !TurboModePlugin.testModeEnabled)
                    {
                        Physics.IgnoreCollision(remainingCollider, removedCollider, adding);
                    }
                }
            }

            if (pruneDestroyed is not null)
            {
                foreach (var destroyed in pruneDestroyed)
                {
                    colliders.Remove(destroyed);
                }
            }

            _tempColliderStorage.Clear();
            VesselSelfCollide_OnPartsChangedVessel.End();
        }

        public void AddPartAdditive(PartBehavior part)
        {
            VesselSelfCollide_AddPartAdditive.Begin(part);
#if TURBOMODE_TRACE_EVENTS
            Debug.Log($"TM: Adding part {part} to {vessel}");
#endif

            WaitForAdditionalColliders.WaitOn(this, part);

            foreach (var existingCollider in colliders)
            {
                VesselSelfCollide_AddPartAdditive.Begin(part);
                if (!existingCollider)
                {
                    _tempColliderStorage.Add(existingCollider);
                    continue;
                }
                foreach (var addedPartCollider in part.Colliders)
                {
                    if (!TurboModePlugin.testModeEnabled)
                    {
                        Physics.IgnoreCollision(addedPartCollider, existingCollider, ignore: true);
                    }
                }
                VesselSelfCollide_AddPartAdditive.End();
            }

            VesselSelfCollide_AddPartAdditive.Begin(part);
            // AddRange creates garbage.
            foreach (var addedPartCollider in part.Colliders)
            {
                colliders.Add(addedPartCollider);
            }
            foreach (var destroyed in _tempColliderStorage)
            {
                colliders.Remove(destroyed);
            }
            _tempColliderStorage.Clear();
            VesselSelfCollide_AddPartAdditive.End();
            VesselSelfCollide_AddPartAdditive.End();
        }

        /// <summary>
        /// Track colliders on this vessel, but don't touch the ignores.
        ///
        /// Useful after vessel split to initialize the new vessel,
        /// when colliders are already ignoring eachother from before the split.
        /// </summary>
        /// <param name="part"></param>
        public void TrackPartsAfterSplit()
        {
            VesselSelfCollide_TrackAfterSplit.Begin();
#if TURBOMODE_TRACE_EVENTS
            Debug.Log($"TM: Assuming {SimulationObject.PartOwner.Parts.Count()} parts for {vessel} already ignore each other.");
#endif
            foreach (PartComponent part in SimulationObject.PartOwner.Parts)
            {
                var partBehavior = Game.SpaceSimulation.ModelViewMap.FromModel(part.SimulationObject).Part;
                foreach (var collider in partBehavior.Colliders)
                {
                    colliders.Add(collider);
                }
            }
            VesselSelfCollide_TrackAfterSplit.End();
        }

        // Gather new colliders when what was changed is unknown.
        // This can be O(n^2) if no colliders were known, so it's the last resort.
        public void FindNewColliders()
        {
            VesselSelfCollide_FindNewColliders.Begin();
#if TURBOMODE_TRACE_EVENTS
            int existingColliderCount = colliders.Count;
#endif
            foreach (var part in SimulationObject.PartOwner.Parts)
            {
                var partBehavior = Game.SpaceSimulation.ModelViewMap.FromModel(part.SimulationObject).Part;
                WaitForAdditionalColliders.WaitOn(this, partBehavior);
                foreach (var addedCollider in partBehavior.Colliders)
                {
                    if (colliders.Contains(addedCollider))
                    {
                        continue;
                    }

                    _tempColliderStorage.Add(addedCollider);
                    foreach (var existingCollider in colliders)
                    {
                        if (!TurboModePlugin.testModeEnabled)
                        {
                            Physics.IgnoreCollision(addedCollider, existingCollider, ignore: true);
                        }
                    }
                }
            }

            foreach (var addedCollider in _tempColliderStorage)
            {
                colliders.Add(addedCollider);
            }
#if TURBOMODE_TRACE_EVENTS
            Debug.Log($"TM: Added {_tempColliderStorage.Count} colliders to existing {existingColliderCount} for {vessel}");
#endif
            _tempColliderStorage.Clear();
            VesselSelfCollide_FindNewColliders.End();
        }

        // Make parts sets from separate vessels ignore each other,
        // assuming that parts on the source vessels were already ignoring eachother.
        public void MergeCombinedVesselColliders(HashSet<Collider> left, HashSet<Collider> right)
        {
            VesselSelfCollide_MergeCombinedVesselColliders.Begin();

            // Assuming none of the parts on separate vessels share the same are the same part
            // That would be weird.
            foreach (var leftCollider in left)
            {
                foreach (var rightCollider in right)
                {
                    if (!TurboModePlugin.testModeEnabled)
                    {
                        Physics.IgnoreCollision(leftCollider, rightCollider, ignore: true);
                    }
                }
            }
            colliders.EnsureCapacity(left.Count + right.Count);
            colliders.UnionWith(left);
            colliders.UnionWith(right);

#if TURBOMODE_TRACE_EVENTS
            Debug.Log($"TM: Created merged vessel {vessel} with {colliders.Count} colliders ({left.Count}+{right.Count}) ({Time.frameCount})");
#endif

            VesselSelfCollide_MergeCombinedVesselColliders.End();
        }

        public void AddCollider(Collider collider)
        {
            VesselSelfCollide_AddCollider.Begin();

            if (colliders.Contains(collider))
            {
                return;
            }

            foreach (var existingCollider in colliders)
            {
                if (!TurboModePlugin.testModeEnabled)
                {
                    Physics.IgnoreCollision(existingCollider, collider, ignore: true);
                }
            }

            VesselSelfCollide_AddCollider.End();
        }

        private static PartBehavior GetPartBehavior(PartComponent modelComponent)
        {
            SimulationObjectModel model = modelComponent.SimulationObject;
            return ((ISimulationObjectView)modelComponent.Game.SpaceSimulation.ModelViewMap.FromModel(model))?.Part;
        }

        #region ObjectComponent required overrides
        [TypeConverterIgnore]
        public override Type Type => typeof(VesselSelfCollide);

        [TypeConverterIgnore]
        public override Type DefinitionType => null;

        [TypeConverterIgnore]
        public override Type StateType => null;

        public override object GetDefinition() => null;

        public override object GetState() => null;

        public override bool ValidateState(object stateData, ISimulationModelMap simulationModelMap) => true;

        public override object SetState(object stateData, ISimulationModelMap simulationModelMap) => null;
        #endregion

        private class WaitForAdditionalColliders : IFixedUpdate
        {
            private VesselSelfCollide vsc;
            private Module_Fairing fairing;

            public static void WaitOn(VesselSelfCollide vsc, PartBehavior partBehavior)
            {
                // In the base game, fairings are the only thing I've found so far that creates colliders
                // some potentially unknown frames later after PartBehaviourInitializedMessage. 
                // I think there is potentially a bug in the game.. CollisionManager seems to happen
                // to catch these new colliders by coincidence from updates triggered by PhysX unpack,
                // but only if Module_Fairing finishes before then.  (Which it seems to usually do, but might not.)
                if (!partBehavior.Modules.TryGetValue(typeof(Module_Fairing), out var fairing))
                {
                    return;
                }

                var fairingModule = fairing as Module_Fairing;

                // Short circuit if fairing is already initialized
                if (fairingModule.ClosedColliders?.Count > 0)
                {
                    AddClosedColliders(vsc, fairingModule);
                    return;
                }

                var waiter = new WaitForAdditionalColliders()
                {
                    vsc = vsc,
                    fairing = fairing as Module_Fairing,
                };

                GameManager.Instance.Game.RegisterFixedUpdate(waiter);
            }

            public void OnFixedUpdate(float deltaTime)
            {
                if (fairing.ClosedColliders?.Count == 0)
                {
                    return;
                }

#if TURBOMODE_TRACE_EVENTS
                Debug.Log($"TM: Adding {fairing.ClosedColliders.Count} colliders from {fairing.part} on {fairing.part.vessel} ({Time.frameCount})");
#endif

                AddClosedColliders(vsc, fairing);

                GameManager.Instance.Game.UnregisterFixedUpdate(this);
            }

            private static void AddClosedColliders(VesselSelfCollide vsc, Module_Fairing fairing)
            {
                foreach (var collider in fairing.ClosedColliders)
                {
                    vsc.AddCollider(collider);
                }
            }
        }
    }
}