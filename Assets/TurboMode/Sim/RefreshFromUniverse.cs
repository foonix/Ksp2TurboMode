using KSP.Sim.impl;
using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace TurboMode.Sim
{
    public partial class RefreshFromUniverse : SystemBase
    {
        protected override void OnUpdate()
        {
            var universeSim = SystemAPI.ManagedAPI.GetSingleton<UniverseRef>();

            foreach (var (part, resourceContainer, simObj) in SystemAPI.Query<RefRO<Part>, RefRW<ResourceContainer>, RefRO<SimObject>>())
            {
                var obj = universeSim.universeModel.FindSimObject(simObj.ValueRO.guid);
            }
        }
    }
}
