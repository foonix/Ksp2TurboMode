using KSP.Sim.ResourceSystem;
using Unity.Entities;


namespace TurboMode.Sim.Components
{
    [InternalBufferCapacity(2)]
    public struct ContainedResource : IBufferElementData
    {
        public readonly ushort type;
        public double amount;

        public ContainedResource(ContainedResourceData crd)
        {
            type = crd.ResourceID.Value;
            amount = crd.StoredUnits;
        }

        public static void CreateOn(EntityManager em, Entity entity, ResourceContainer kspContainer)
        {
            var count = kspContainer.GetResourcesContainedCount();
            var buffer = em.AddBuffer<ContainedResource>(entity);
            buffer.EnsureCapacity(count);

            foreach (var thing in kspContainer.GetAllResourcesContainedData())
            {
                buffer.Add(new ContainedResource(thing));
            }
        }
    }
}