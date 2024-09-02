using TurboMode.Sim.Components;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace TurboMode.Sim.Systems
{
    [BurstCompile]
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

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            massUpdateQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAllRW<RigidbodyComponent>()
                .WithAll<Part>()
                .Build(ref state);
            rigidbodyComponentHandle = state.GetComponentTypeHandle<RigidbodyComponent>(false);
            partHandle = state.GetComponentTypeHandle<Part>(false);
            kerbalStorageHandle = state.GetComponentTypeHandle<KerbalStorage>(true);
            containedResourceHandle = state.GetBufferTypeHandle<ContainedResource>(true);
            massModifiersHandle = state.GetComponentTypeHandle<MassModifiers>(true);
        }
        public readonly void OnDestroy(ref SystemState state) { }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            rigidbodyComponentHandle.Update(ref state);
            partHandle.Update(ref state);
            kerbalStorageHandle.Update(ref state);
            containedResourceHandle.Update(ref state);

            var resourceTypes = SystemAPI.GetSingletonBuffer<ResourceTypeData>(true);
            var partDefinitions = SystemAPI.GetSingletonBuffer<PartDefintionData>(true);

            var updateMassHandle = new UpdateMassChunks()
            {
                rigidbodyComponentHandle = rigidbodyComponentHandle,
                partHandle = partHandle,
                kerbalStorageHandle = kerbalStorageHandle,
                containedResourceHandle = containedResourceHandle,
                resourceTypeBuffer = resourceTypes,
                massModifiersHandle = massModifiersHandle,
                partDefinitions = partDefinitions,
            }.ScheduleParallel(massUpdateQuery, state.Dependency);

            var update = new UpdateVesselPhysicsStats()
            {
                rbcs = SystemAPI.GetComponentLookup<RigidbodyComponent>(),
            }.ScheduleParallel(updateMassHandle);

            updateMassHandle.Complete();
            update.Complete();
        }

        /// <summary>
        /// Update Part and Rigidbody data together.
        /// </summary>
        [BurstCompile]
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
            public DynamicBuffer<PartDefintionData> partDefinitions;

            [BurstCompile]
            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<RigidbodyComponent> rigidbodies = chunk.GetNativeArray(ref rigidbodyComponentHandle);
                NativeArray<Part> parts = chunk.GetNativeArray(ref partHandle);
                NativeArray<MassModifiers> modifiers = chunk.GetNativeArray(ref massModifiersHandle);
                NativeArray<KerbalStorage> kerbals = chunk.GetNativeArray(ref kerbalStorageHandle);
                BufferAccessor<ContainedResource> storedResources = chunk.GetBufferAccessor(ref containedResourceHandle);

                var partEnumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);

                bool hasMassModifiers = chunk.Has<MassModifiers>();
                bool hasKerbalStorage = chunk.Has<KerbalStorage>();
                bool hasContainedResources = chunk.Has<ContainedResource>();

                while (partEnumerator.NextEntityIndex(out var i))
                {
                    var rbc = rigidbodies[i];
                    var part = parts[i];

                    // start from base mass value for the part.
                    rbc.effectiveMass = partDefinitions[part.typeId].mass;

                    if (hasMassModifiers)
                    {
                        rbc.effectiveMass += modifiers[i].mass;
                    }

                    // The clamp must be applied even if the base part mass is below minium. (see small solar panels)
                    if (rbc.effectiveMass < MINIMUM_PART_MASS)
                    {
                        rbc.effectiveMass = MINIMUM_PART_MASS;
                    }

                    // It's a bit silly, but not every Rigidbody has a part, parts might not have a Rigidbody,
                    // and both need mass data.
                    part.dryMass = rbc.effectiveMass;

                    if (hasKerbalStorage)
                    {
                        var greenMass = kerbals[i].count * MASS_PER_KERBAL;
                        part.greenMass = greenMass;
                        rbc.effectiveMass += greenMass;
                    }
                    else
                    {
                        part.greenMass = 0;
                    }

                    if (hasContainedResources)
                    {
                        var stored = storedResources[i];
                        double resourceMass = 0;

                        foreach (var storedResource in stored)
                        {
                            var typeData = resourceTypeBuffer[storedResource.type];
                            resourceMass += typeData.massPerUnit * storedResource.amount;
                        }
                        rbc.effectiveMass += resourceMass;
                        part.wetMass = resourceMass;
                    }
                    else
                    {
                        part.wetMass = 0;
                    }

                    rigidbodies[i] = rbc;
                }
            }
        }

        [BurstCompile]
        public partial struct UpdateVesselPhysicsStats : IJobEntity
        {
            [ReadOnly] public ComponentLookup<RigidbodyComponent> rbcs;

            void Execute(ref Vessel vessel, in DynamicBuffer<OwnedPartRef> ownedParts)
            {
                Vector3d moment = Vector3d.zero;
                Vector3d momentum = Vector3d.zero;
                Vector3d angularMoment = Vector3d.zero;
                double totalMass = 0;
                double reEntryMaximumFlux = 0;

                foreach (var partEntity in ownedParts)
                {
                    var rbc = rbcs[partEntity.partEntity];
                    if (rbc.physicsMode == KSP.Sim.PartPhysicsModes.None)
                    {
                        continue;
                    }

                    moment += rbc.localToOwner.TransformPoint(rbc.centerOfMass) * rbc.effectiveMass;
                    momentum += rbc.localToOwner.TransformVector(rbc.velocity) * rbc.effectiveMass;
                    angularMoment += rbc.angularVelocity * rbc.effectiveMass;

                    reEntryMaximumFlux = math.max(reEntryMaximumFlux, rbc.reEntryMaximumFlux);

                    totalMass += rbc.effectiveMass;
                }

                if (totalMass > 0)
                {
                    vessel.centerOfMass = moment / totalMass;
                    vessel.velocityMassAvg = momentum / totalMass;
                    vessel.angularVelocityMassAvg = angularMoment / totalMass;

                    Matrix4x4 left = Matrix4x4.zero;
                    Matrix4x4 m = Matrix4x4.identity;
                    Matrix4x4 m2 = Matrix4x4.identity;
                    Matrix4x4 m3 = Matrix4x4.identity;

                    foreach (var partEntity in ownedParts)
                    {
                        var rbc = rbcs[partEntity.partEntity];

                        KSPUtil.ToDiagonalMatrix2(rbc.inertiaTensor, ref m);
                        Quaternion q = QuaternionD.Inverse(rbc.inertiaTensorRotation);
                        Matrix4x4 matrix4x = Matrix4x4.TRS(Vector3.zero, rbc.inertiaTensorRotation, Vector3.one);
                        Matrix4x4 matrix4x2 = Matrix4x4.TRS(Vector3.zero, q, Vector3.one);
                        KSPUtil.Add(ref left, matrix4x * m * matrix4x2);
                        Vector3d vector3d = rbc.localToOwner.TransformPoint(rbc.centerOfMass) - vessel.centerOfMass;
                        KSPUtil.ToDiagonalMatrix2((float)(rbc.effectiveMass * vector3d.sqrMagnitude), ref m2);
                        KSPUtil.Add(ref left, m2);
                        KSPUtil.OuterProduct2(vector3d, (0f - rbc.effectiveMass) * vector3d, ref m3);
                        KSPUtil.Add(ref left, m3);
                    }
                    vessel.momentOfInertia = KSPUtil.Diag(left);
                }
                vessel.totalMass = totalMass;
                vessel.reEntryMaximumFlux = reEntryMaximumFlux;
            }
        }
    }
}