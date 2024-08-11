using KSP.Game;
using KSP.Sim.ResourceSystem;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace TurboMode.Sim
{
    public partial class ResourceManagementSystem : SystemBase
    {
        Entity resourceTypeDb;

        protected override void OnCreate()
        {
            var rdd = GameManager.Instance.Game.ResourceDefinitionDatabase;

            resourceTypeDb = ResourceTypeData.BuildDbSingleton(rdd, EntityManager);
        }

        protected override void OnUpdate()
        {

        }
    }
}
