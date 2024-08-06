using KSP.Sim.impl;
using Unity.Entities;
using UnityEngine;

namespace TurboMode.Sim
{
    /// <summary>
    /// Track the game's entity model and triger structural change events into ECS
    /// </summary>
    public class SpaceSimulationMonitor
    {
        private readonly UniverseSim universeSim;

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
            };
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
                };
                simObj.onComponentRemoved += (type, component) =>
                {
                    Debug.Log($"TM: UniversModel: Sim object {trackedObject} removed component {type} {simObj} ({Time.frameCount})");
                };
            }
        }
    }
}
