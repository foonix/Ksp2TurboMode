using KSP.Sim.impl;
using UnityEngine;

namespace TurboMode.Sim
{
    public class SpaceSimulationMonitor
    {
        public SpaceSimulationMonitor(SpaceSimulation spaceSim)
        {
#if TURBOMODE_TRACE_EVENTS
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
            };
            spaceSim.UniverseModel.onSimulationObjectRemoved += obj =>
            {
                Debug.Log($"TM: UniversModel: Sim object removed {obj} ({Time.frameCount})");
            };
#endif
        }
    }
}
