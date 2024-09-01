using KSP.Game;
using KSP.Sim;
using KSP.Sim.impl;
using KSP.Sim.ResourceSystem;
using System.Collections.Generic;
using System.Reflection;
using TurboMode.Sim.Components;
using Unity.Entities;

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

        protected override void OnCreate()
        {
            var rdd = GameManager.Instance.Game.ResourceDefinitionDatabase;

            resourceTypeDb = ResourceTypeData.BuildDbSingleton(rdd, EntityManager);
        }

        protected override void OnUpdate()
        {
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

            Entities
                .WithName("ScrapePartSimPositions")
                .ForEach((ref Part part, in SimObject simObject) =>
                {
                    var physicsSpace = GameManager.Instance.Game.UniverseView.PhysicsSpace;
                    var bodyframe = simObject.inUniverse.transform.bodyFrame;
                    var partComponent = simObject.inUniverse.Part;
                    var rbc = simObject.inUniverse.Rigidbody;

                    if (simObject.owner != Entity.Null)
                    {
                        var owner = EntityManager.GetComponentData<SimObject>(simObject.owner).inUniverse;

                        part.localToOwner = Patches.BurstifyTransformFrames.ComputeTransformFromOther(owner.transform.bodyFrame as TransformFrame, bodyframe);
                    }
                    else
                    {
                        part.localToOwner = Matrix4x4D.Identity();
                    }

                    part.centerOfMass = partComponent.CenterOfMass.localPosition;
                    // probably don't want to involve the physics space matrix here.  Maybe on the output side at the vessel level?
                    part.velocity = physicsSpace.VelocityToPhysics(rbc.Velocity, rbc.Position);
                    part.angularVelocity = physicsSpace.AngularVelocityToPhysics(rbc.AngularVelocity);
                    part.reEntryMaximumFlux = partComponent.ThermalData.ReentryFlux;
                    part.physicsMode = partComponent.PhysicsMode;
                })
                .WithoutBurst()
                .Run();
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
