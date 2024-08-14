using TurboMode.Sim.Components;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;

namespace TurboMode.Sim.Systems
{
    public partial struct PhysicsDataUpdate : ISystem
    {
        EntityQuery massUpdateQuery;

        ComponentTypeHandle<RigidbodyComponent> rigidbodyComponentHandle;
        ComponentTypeHandle<Part> partHandle;
        ComponentTypeHandle<KerbalStorage> kerbalStorageHandle;
        ComponentTypeHandle<MassModifiers> massModifiersHandle;
        BufferTypeHandle<ContainedResource> containedResourceHandle;

        // Game.SessionManager.KerbalRosterManager.KerbalIVAMass
        private const double MASS_PER_KERBAL = 0.095;
        // PhysicsSettings.PHYSX_MINIMUM_PART_MASS
        private const double MINIMUM_PART_MASS = 0.001;

        public void OnCreate(ref SystemState state)
        {
            massUpdateQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAllRW<RigidbodyComponent>()
                .WithAll<Part>()
                .Build(ref state);
            rigidbodyComponentHandle = state.GetComponentTypeHandle<RigidbodyComponent>(false);
            partHandle = state.GetComponentTypeHandle<Part>(true);
            kerbalStorageHandle = state.GetComponentTypeHandle<KerbalStorage>(true);
            containedResourceHandle = state.GetBufferTypeHandle<ContainedResource>(true);
            massModifiersHandle = state.GetComponentTypeHandle<MassModifiers>(true);
        }
        public readonly void OnDestroy(ref SystemState state) { }

        public void OnUpdate(ref SystemState state)
        {
            rigidbodyComponentHandle.Update(ref state);
            partHandle.Update(ref state);
            kerbalStorageHandle.Update(ref state);
            containedResourceHandle.Update(ref state);

            var resourceTypes = SystemAPI.GetSingletonBuffer<ResourceTypeData>(true);

            new UpdateMassChunks()
            {
                rigidbodyComponentHandle = rigidbodyComponentHandle,
                partHandle = partHandle,
                kerbalStorageHandle = kerbalStorageHandle,
                containedResourceHandle = containedResourceHandle,
                resourceTypeBuffer = resourceTypes,
                massModifiersHandle = massModifiersHandle,
            }.Run(massUpdateQuery);
        }

        private partial struct UpdateMassChunks : IJobChunk
        {
            // per entity
            public ComponentTypeHandle<RigidbodyComponent> rigidbodyComponentHandle;
            public ComponentTypeHandle<Part> partHandle;
            public ComponentTypeHandle<KerbalStorage> kerbalStorageHandle;
            public BufferTypeHandle<ContainedResource> containedResourceHandle;
            public ComponentTypeHandle<MassModifiers> massModifiersHandle;

            // singleton
            public DynamicBuffer<ResourceTypeData> resourceTypeBuffer;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<RigidbodyComponent> rigidbodies = chunk.GetNativeArray(ref rigidbodyComponentHandle);
                NativeArray<Part> parts = chunk.GetNativeArray(ref partHandle);
                var partEnumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);

                while (partEnumerator.NextEntityIndex(out var i))
                {
                    var rbc = rigidbodies[i];
                    rbc.effectiveMass = parts[i].dryMass;
                    // The clamp must be applied even if the base part mass is below minium. (see small solar panels)
                    if (rbc.effectiveMass < MINIMUM_PART_MASS)
                    {
                        rbc.effectiveMass = MINIMUM_PART_MASS;
                    }
                    rigidbodies[i] = rbc;
                }

                if (chunk.Has<MassModifiers>())
                {
                    NativeArray<MassModifiers> modifiers = chunk.GetNativeArray(ref massModifiersHandle);
                    var storageEnumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                    while (storageEnumerator.NextEntityIndex(out var i))
                    {
                        var rbc = rigidbodies[i];
                        rbc.effectiveMass += modifiers[i].mass;
                        // The original code applies the clamp just after the modifiers, which can be negative.
                        // We may be double clamping here if a tiny part also has mass modifiers, but I'm not sure that happens.
                        if (rbc.effectiveMass < MINIMUM_PART_MASS)
                        {
                            rbc.effectiveMass = MINIMUM_PART_MASS;
                        }
                        rigidbodies[i] = rbc;
                    }
                }

                if (chunk.Has<KerbalStorage>())
                {
                    NativeArray<KerbalStorage> kerbals = chunk.GetNativeArray(ref kerbalStorageHandle);
                    var storageEnumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                    while (storageEnumerator.NextEntityIndex(out var i))
                    {
                        var rbc = rigidbodies[i];
                        rbc.effectiveMass += kerbals[i].count * MASS_PER_KERBAL;
                        rigidbodies[i] = rbc;
                    }
                }

                if (chunk.Has<ContainedResource>())
                {
                    BufferAccessor<ContainedResource> storedResources = chunk.GetBufferAccessor(ref containedResourceHandle);
                    for (int i = 0; i < storedResources.Length; i++)
                    {
                        var rbc = rigidbodies[i];
                        var stored = storedResources[i];

                        double resourceMass = 0;

                        foreach (var storedResource in stored)
                        {
                            var typeData = resourceTypeBuffer[storedResource.type];
                            resourceMass += typeData.massPerUnit * storedResource.amount;
                        }
                        rbc.effectiveMass += resourceMass;
                        rigidbodies[i] = rbc;
                    }
                }
            }
        }
    }
}