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
                    var resourceHolder = universeSim.universeModel.FindSimObject(simObject.guid);
                    var prc = resourceHolder.Part.PartResourceContainer;

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
                    var simObj = universeSim.universeModel.FindSimObject(simObject.guid);
                    foreach (var module in simObj.Part.Modules.Values)
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
