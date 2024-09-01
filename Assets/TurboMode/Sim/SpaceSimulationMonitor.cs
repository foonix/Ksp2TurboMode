using KSP.Game;
using KSP.Messages;
using KSP.Sim.impl;
using MonoMod.RuntimeDetour;
using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace TurboMode.Sim
{
    /// <summary>
    /// Track the game's entity model and triger structural change events into ECS
    /// </summary>
    public class SpaceSimulationMonitor : IDisposable
    {
        private readonly UniverseSim universeSim;

        private Hook onViewObjectDestroyedHook;

        public SpaceSimulationMonitor(SpaceSimulation spaceSim, UniverseSim universeSim)
        {
            this.universeSim = universeSim;
            spaceSim.UniverseModel.onVesselAdded += vessel =>
            {
                Debug.Log($"TM: UniversModel: Vessel added {vessel} ({Time.frameCount})");
            };
            spaceSim.UniverseModel.onVesselRemoved += vessel =>
            {
                Debug.Log($"TM: UniversModel: Vessel removed {vessel} ({Time.frameCount})");
            };
            spaceSim.UniverseModel.onPartAdded += part =>
            {
                Debug.Log($"TM: UniversModel: Part added {part} ({Time.frameCount})");
            };
            spaceSim.UniverseModel.onVesselRemoved += part =>
            {
                Debug.Log($"TM: UniversModel: Part removed {part} ({Time.frameCount})");
            };
            spaceSim.UniverseModel.onSimulationObjectAdded += obj =>
            {
                Debug.Log($"TM: UniversModel: Sim object added {obj} ({Time.frameCount})");
                var entity = universeSim.AddSimObj(obj);
                new SimComponentMonitor(universeSim, obj, entity);
            };
            spaceSim.UniverseModel.onSimulationObjectRemoved += obj =>
            {
                Debug.Log($"TM: UniversModel: Sim object removed {obj} ({Time.frameCount})");
                universeSim.RemoveSimObject(obj);
            };
            GameManager.Instance.Game.Messages.Subscribe<SimulationModelAndViewBoundMessage>(OnViewObjectBound);
            // There is no callback for when a view object is destroyed or unbound.
            onViewObjectDestroyedHook = new(
                typeof(SimulationObjectView).GetMethod("Destroy"),
                (Action<Action<SimulationObjectView>, SimulationObjectView>)OnViewObjectDestroyed
            );
        }

        private void OnViewObjectBound(MessageCenterMessage message)
        {
            var boundMessage = (SimulationModelAndViewBoundMessage)message;
            Debug.Log($"TM: UniversModel: View object bound sim:{boundMessage.SimObjectModel.GlobalId} {boundMessage.SimObjectView.gameObject.name} ({Time.frameCount})");
            // SimulationObjectView is the only thing that implements ISimulationObjectView.
            universeSim.AddBoundViewObj(boundMessage.SimObjectModel, (SimulationObjectView)boundMessage.SimObjectView);
        }

        private void OnViewObjectDestroyed(Action<SimulationObjectView> orig, SimulationObjectView obj)
        {
            Debug.Log($"TM: UniversModel: View object destroyed {obj.gameObject.name} ({Time.frameCount})");
            universeSim.RemoveViewObj(obj);
            orig(obj);
        }

        private class SimComponentMonitor
        {
            readonly SimulationObjectModel trackedObject;
            readonly Entity entity;
            readonly UniverseSim universeSim;
            public SimComponentMonitor(UniverseSim universeSim, SimulationObjectModel simObj, Entity entity)
            {
                trackedObject = simObj;
                this.entity = entity;
                this.universeSim = universeSim;
                simObj.onComponentAdded += (type, component) =>
                {
                    Debug.Log($"TM: UniversModel: Sim object {trackedObject} new component {type} {simObj} ({Time.frameCount})");
                    universeSim.AddComponent(entity, component);

                    if (component is PartOwnerComponent ownerComponent)
                    {
                        simObj.PartOwner.PartsAdded += (added) =>
                        {
                            Debug.Log($"TM: UniversModel: Sim object {trackedObject} parts added to owner {added.Count} ({Time.frameCount})");
                            BulkChangeOwner(added);
                        };
                        simObj.PartOwner.PartsRemoved += (removed) =>
                        {
                            Debug.Log($"TM: UniversModel: Sim object {trackedObject} parts removed from owner {removed.Count} ({Time.frameCount})");
                            BulkChangeOwner(removed);
                        };
                    }
                };
                simObj.onComponentRemoved += (type, component) =>
                {
                    Debug.Log($"TM: UniversModel: Sim object {trackedObject} removed component {type} {simObj} ({Time.frameCount})");
                };

                simObj.onViewLoad += (vo) =>
                {
                    // This doesn't seem to actually work, at least not when loading a saved game into flight.
                    Debug.Log($"TM: UniversModel: Sim object {trackedObject} view loaded {vo} ({Time.frameCount})");
                };
            }

            private void BulkChangeOwner(List<PartComponent> partComponents)
            {
                foreach (var part in partComponents)
                {
                    universeSim.ChangeOwner(part.GlobalId, entity);
                }
            }
        }

        public void Dispose()
        {
            onViewObjectDestroyedHook?.Dispose();
            onViewObjectDestroyedHook = null;
        }

        ~SpaceSimulationMonitor()
        {
            Dispose();
        }
    }
}
