using KSP.Game;
using TurboMode.Sim.Components;
using Unity.Entities;

namespace TurboMode.Sim.Systems
{
    public partial class ResourceManagementSystem : SystemBase
    {
        Entity resourceTypeDb;

        protected override void OnCreate()
        {
            var rdd = GameManager.Instance.Game.ResourceDefinitionDatabase;

            resourceTypeDb = ResourceTypeData.BuildDbSingleton(rdd, EntityManager);
            PartDefintionData.BuildDbSingleton(EntityManager);
        }

        protected override void OnUpdate()
        {
            var partData = SystemAPI.GetSingletonBuffer<PartDefintionData>();
            var partDataMap = SystemAPI.ManagedAPI.GetSingleton<PartDefintionData.PartNameToDataIdMap>();
        }
    }
}
