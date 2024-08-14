using TurboMode.Sim.Components;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;

namespace TurboMode.Sim.Systems
{
    public partial struct PhysicsDataUpdate : ISystem
    {
        EntityQuery massUpdateQuery;

        public ComponentTypeHandle<RigidbodyComponent> rigidbodyComponentHandle;
        public ComponentTypeHandle<Part> partHandle;
        public ComponentTypeHandle<KerbalStorage> kerbalStorageHandle;

        public void OnCreate(ref SystemState state)
        {
            massUpdateQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAllRW<RigidbodyComponent>()
                .WithAll<Part>()
                .Build(ref state);
            rigidbodyComponentHandle = state.GetComponentTypeHandle<RigidbodyComponent>(false);
            partHandle = state.GetComponentTypeHandle<Part>(true);
            kerbalStorageHandle = state.GetComponentTypeHandle<KerbalStorage>(true);
        }
        public readonly void OnDestroy(ref SystemState state) { }

        public void OnUpdate(ref SystemState state)
        {
            rigidbodyComponentHandle.Update(ref state);
            partHandle.Update(ref state);
            kerbalStorageHandle.Update(ref state);

            new UpdateMassChunks()
            {
                rigidbodyComponentHandle = rigidbodyComponentHandle,
                partHandle = partHandle,
                kerbalStorageHandle = kerbalStorageHandle,
            }.Run(massUpdateQuery);
        }

        private partial struct UpdateMassChunks : IJobChunk
        {
            public ComponentTypeHandle<RigidbodyComponent> rigidbodyComponentHandle;
            public ComponentTypeHandle<Part> partHandle;
            public ComponentTypeHandle<KerbalStorage> kerbalStorageHandle;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<RigidbodyComponent> rigidbodies = chunk.GetNativeArray(ref rigidbodyComponentHandle);
                NativeArray<Part> parts = chunk.GetNativeArray(ref partHandle);
                var partEnumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);

                while (partEnumerator.NextEntityIndex(out var i))
                {
                    var rbc = rigidbodies[i];
                    rbc.effectiveMass = parts[i].dryMass;
                    rigidbodies[i] = rbc;
                }

                NativeArray<KerbalStorage> kerbals = chunk.GetNativeArray(ref kerbalStorageHandle);
                if (kerbals.IsCreated)
                {
                    var storageEnumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                    while (storageEnumerator.NextEntityIndex(out var i))
                    {
                        var rbc = rigidbodies[i];
                        // TODO: actual kerbal mass
                        rbc.effectiveMass += kerbals[i].count * 300f;
                        rigidbodies[i] = rbc;
                    }
                }
            }
        }
    }
}