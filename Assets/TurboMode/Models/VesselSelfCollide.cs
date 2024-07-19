using KSP.Sim;
using KSP.Sim.Converters;
using KSP.Sim.impl;
using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace TurboMode.Models
{
    public class VesselSelfCollide : ObjectComponent
    {
        [TypeConverterIgnore]
        public readonly HashSet<Collider> colliders = new();

        private readonly VesselComponent vessel;
        private readonly List<Collider> _tempColliderStorage = new(100);

        static readonly ProfilerMarker VesselSelfCollide_PartChange = new("VesselSelfCollide Part Changes");

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

        public override void OnRemoved(SimulationObjectModel simulationObject, double universalTime)
        {
            if (SimulationObject.PartOwner != null)
            {
                SimulationObject.PartOwner.PartsRemoved -= OnPartsRemoved;
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
            VesselSelfCollide_PartChange.Begin(SimulationObject.objVesselBehavior);
            Debug.Log(string.Format("TM: {0} {1} parts for {2}",
                adding ? "Adding" : "Removing",
                parts.Count,
                vessel.Name));

            // not separating parts from each other on the assumption that each group
            // removed is going to the same vessel or debris
            foreach (var partComponent in parts)
            {
                var part = GetPartBehavior(partComponent);
                if (!part) continue;

                foreach (var removingCollider in part.Colliders)
                {
                    bool canged = adding ? colliders.Add(removingCollider) : colliders.Remove(removingCollider);
                    if (canged)
                    {
                        _tempColliderStorage.Add(removingCollider);
                    }
                }
            }

            foreach (var removedCollider in _tempColliderStorage)
            {
                foreach (var remainingCollider in colliders)
                {
                    if (!TurboModePlugin.testMode)
                    {
                        Physics.IgnoreCollision(remainingCollider, removedCollider, adding);
                    }
                }
            }

            _tempColliderStorage.Clear();
            VesselSelfCollide_PartChange.End();
        }

        public void AddPartAdditive(PartBehavior part)
        {
            VesselSelfCollide_PartChange.Begin(part);
            Debug.Log($"TM: Adding part {part} to {vessel}");

            foreach (var addedPartCollider in part.Colliders)
            {
                var addedPartColliderRigidbody = addedPartCollider.attachedRigidbody;

                foreach (var existingCollider in colliders)
                {
                    if (!System.Object.ReferenceEquals(addedPartColliderRigidbody, existingCollider.attachedRigidbody) && !TurboModePlugin.testMode)
                    {
                        Physics.IgnoreCollision(addedPartCollider, existingCollider, ignore: true);
                    }
                }
            }

            // AddRange creates garbage.
            foreach (var addedPartCollider in part.Colliders)
            {
                colliders.Add(addedPartCollider);
            }
            VesselSelfCollide_PartChange.End();
        }

        /// <summary>
        /// Track colliders on this vessel, but don't touch the ignores.
        /// 
        /// Useful after vessel split, when colliders are already ignoring eachother from before the split.
        /// </summary>
        /// <param name="part"></param>
        public void TrackPartsAfterSplit()
        {
            foreach (PartComponent part in base.SimulationObject.PartOwner.Parts)
            {
                var partBehavior = Game.SpaceSimulation.ModelViewMap.FromModel(part.SimulationObject).Part;
                foreach (var collider in partBehavior.Colliders)
                {
                    colliders.Add(collider);
                }
            }
        }

        private static PartBehavior GetPartBehavior(PartComponent modelComponent)
        {
            SimulationObjectModel model = modelComponent.SimulationObject;
            return ((ISimulationObjectView)modelComponent.Game.SpaceSimulation.ModelViewMap.FromModel(model))?.Part;
        }

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
    }
}