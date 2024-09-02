using KSP.Game;
using KSP.Sim;
using KSP.Sim.impl;
using KSP.Sim.ResourceSystem;
using System.Collections.Generic;
using System.Reflection;
using TurboMode.Sim.Components;
using Unity.Entities;
using Unity.Profiling;

namespace TurboMode.Sim.Systems
{
    public partial class ResourceManagementSystem : SystemBase
    {
        Entity resourceTypeDb;

        private static readonly ReflectionUtil.FieldHelper<KerbalRosterManager, Dictionary<IGGuid, List<KerbalInfo>>> _lookupSimObjectTableField
            = new(typeof(KerbalRosterManager).GetField("_lookupSimObjectTable", BindingFlags.NonPublic | BindingFlags.Instance));

        private static readonly ReflectionUtil.FieldHelper<ResourceContainer, List<ResourceDefinitionID>> _resourceIDMapField
            = new(typeof(ResourceContainer).GetField("_resourceIDMap", BindingFlags.NonPublic | BindingFlags.Instance));
        private static readonly ReflectionUtil.FieldHelper<ResourceContainer, List<double>> _storedUnitsLookupField
            = new(typeof(ResourceContainer).GetField("_storedUnitsLookup", BindingFlags.NonPublic | BindingFlags.Instance));

        private static readonly ProfilerMarker s_scrapeSimPositionsMarker = new("ScrapePartSimPositions");

        protected override void OnCreate()
        {
            var rdd = GameManager.Instance.Game.ResourceDefinitionDatabase;

            resourceTypeDb = ResourceTypeData.BuildDbSingleton(rdd, EntityManager);
        }

        protected override void OnUpdate()
        {
            var game = GameManager.Instance.Game;
            if (!game.IsSimulationRunning())
            {
                return;
            }
            var physicsSpace = game.UniverseView.PhysicsSpace;

            var partData = SystemAPI.GetSingletonBuffer<PartDefintionData>();
            var partDataMap = SystemAPI.ManagedAPI.GetSingleton<PartDefintionData.PartNameToDataIdMap>();
            var universeSim = SystemAPI.ManagedAPI.GetSingleton<UniverseRef>();

            // Update resource amounts, implicitly skipping objects that don't contain them.
            Entities
                .WithName("ScrapeResourceAmounts")
                .ForEach((ref DynamicBuffer<ContainedResource> resourceContainer, in SimObject simObject) =>
                {
                    var prc = simObject.inUniverse.Part.PartResourceContainer;

                    UpdateResources(ref resourceContainer, prc);
                })
                .WithoutBurst()
                .Run();

            Entities
                .WithName("ScrapeCrewAmounts")
                .ForEach((ref KerbalStorage kerbalStorage, in SimObject simObject) =>
                {
                    // Their count accessor clones the the list I'm getting here and then returns the count of the cloned list.
                    // If there is no dictionary entry, an empty list is returned and it uses the count from the empty list.
                    // Byapss the accessor and just get the list count directly.
                    var lookup = _lookupSimObjectTableField.Get(GameManager.Instance.Game.SessionManager.KerbalRosterManager);
                    if (lookup.TryGetValue(simObject.guid, out var kerbalsInObject))
                    {
                        kerbalStorage.count = (ushort)kerbalsInObject.Count;
                    }
                    else
                    {
                        kerbalStorage.count = 0;
                    }
                })
                .WithoutBurst()
                .Run();

            // Maybe move this to RigidbodySystem and collect more information for the rigidbody.
            // Leaving for here now to collect everything to drive mass updates.
            Entities
                .WithName("ScrapeMassModifiers")
                .ForEach((ref MassModifiers massModifiers, in SimObject simObject) =>
                {
                    double massFound = 0;
                    foreach (var module in simObject.inUniverse.Part.Modules.Values)
                    {
                        foreach (var moduleData in module.DataModules.Values)
                        {
                            if (moduleData is IMassModifier modifier)
                            {
                                massFound += modifier.MassModifier;
                            }
                        }
                    }
                    // note: modifiers can be negative.  The minimum mass is clamped later.
                    massModifiers.mass = massFound;
                })
                .WithoutBurst()
                .Run();

            // This is basically the read steps in PartOwnerComponent.CalculatePhysicsStats(),
            // except we don't need to read the mass because we're calculating it from the resource totals.
            // We'll still total the mass offthread, but most (all?) of the motion/tensor data doesn't matter
            // for background vessels, skip part reads that aren't needed.
            foreach (var (ownedParts, vesselSimObject) in SystemAPI.Query<DynamicBuffer<OwnedPartRef>, SimObject>())
            {
                using var marker = s_scrapeSimPositionsMarker.Auto();
                var ownerFrame = vesselSimObject.inUniverse.transform.bodyFrame;
                var vesselPhysicsMode = vesselSimObject.inUniverse.Vessel.Physics;

                if (vesselPhysicsMode != PhysicsMode.RigidBody)
                {
                    continue;
                }

                foreach (var ownedPart in ownedParts)
                {
                    var part = SystemAPI.GetComponent<Part>(ownedPart.partEntity);
                    var partSimObject = SystemAPI.ManagedAPI.GetComponent<SimObject>(ownedPart.partEntity);

                    var partComponent = partSimObject.inUniverse.Part;
                    var rbc = partSimObject.inUniverse.Rigidbody;

                    if (rbc.PhysicsMode != PartPhysicsModes.None)
                    {
                        part.localToOwner = Patches.BurstifyTransformFrames.ComputeTransformFromOther(ownerFrame as TransformFrame, ownerFrame);

                        part.centerOfMass = partComponent.CenterOfMass.localPosition;
                        // probably don't want to involve the physics space matrix here.  Maybe on the output side at the vessel level?
                        part.angularVelocity = physicsSpace.AngularVelocityToPhysics(rbc.AngularVelocity);
                        part.velocity = physicsSpace.VelocityToPhysics(rbc.Velocity, rbc.Position);
                    }

                    part.reEntryMaximumFlux = partComponent.ThermalData.ReentryFlux;
                    part.physicsMode = partComponent.PhysicsMode;

                    SystemAPI.SetComponent(ownedPart.partEntity, part);
                }
            }
        }

        private static void UpdateResources(ref DynamicBuffer<ContainedResource> resourceContainer, ResourceContainer prc)
        {
            var typeIds = _resourceIDMapField.Get(prc);
            var storedAmounts = _storedUnitsLookupField.Get(prc);

            resourceContainer.Clear();
            for (int i = 0; i < typeIds.Count; i++)
            {
                resourceContainer.Add(new ContainedResource(typeIds[i].Value, storedAmounts[i]));
            }
        }
    }
}
