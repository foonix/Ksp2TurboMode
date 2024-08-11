using KSP.Sim.ResourceSystem;
using System;
using System.Linq;
using Unity.Entities;

namespace TurboMode.Sim.Components
{
    [InternalBufferCapacity(20)]
    public readonly struct ResourceTypeData : IBufferElementData
    {
        public readonly double massPerUnit;
        public readonly double specificHeatCapacityPerUnit;
        public readonly Flags flags;

        [Flags]
        public enum Flags
        {
            None = 0,
            IgnoreForIsp = 1,
        }

        public ResourceTypeData(ResourceDefinitionData resourceDefinitionData)
        {
            var rp = resourceDefinitionData.resourceProperties;
            massPerUnit = rp.massPerUnit;
            specificHeatCapacityPerUnit = rp.specificHeatCapacityPerUnit;

            flags = default;
            if (rp.ignoreForIsp) flags |= Flags.IgnoreForIsp;
        }

        public static Entity BuildDbSingleton(ResourceDefinitionDatabase rdd, EntityManager em)
        {
            var rtddb = em.CreateSingletonBuffer<ResourceTypeData>("ResourceTypeDataDb");
            var buffer = em.GetBuffer<ResourceTypeData>(rtddb, false);

            var resourceIds = rdd.GetAllResourceIDs().ToArray();

            // IDs are 1-indexed.  assuming they are consecutive..
            buffer.Length = resourceIds.Length + 1;

            // leaving blank spots in the array so that lookup can be simply keyed by ID.
            foreach (var resourceId in resourceIds)
            {
                var resourceData = rdd.GetDefinitionData(resourceId);

                if (!resourceData.IsRecipe)
                {
                    buffer[resourceId.Value] = new(resourceData);
                }
            }

            return rtddb;
        }
    }
}
