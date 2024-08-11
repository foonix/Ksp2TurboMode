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
            var universeSim = SystemAPI.ManagedAPI.GetSingleton<UniverseRef>();

            // Update resource amounts, mplicitly skipping objects that don't contain them.
            Entities
                .WithName("ScrapeResourceAmounts")
                .ForEach((ref DynamicBuffer<ContainedResource> resourceContainer, in SimObject simObject) =>
                {
                    var resourceHolder = universeSim.universeModel.FindSimObject(simObject.guid);
                    var prc = resourceHolder.Part.PartResourceContainer;
                    var count = prc.GetResourcesContainedCount();

                    // TODO: Fix allocs from GetAllResourcesContainedData()
                    resourceContainer.Clear();
                    foreach (var containedResource in prc.GetAllResourcesContainedData())
                    {
                        resourceContainer.Add(new ContainedResource(containedResource));
                    }
                })
                .WithoutBurst()
                .Run();
        }
    }
}
