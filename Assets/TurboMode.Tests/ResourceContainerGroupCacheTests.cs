using KSP.Sim.ResourceSystem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using TurboMode.Models;
using System.Collections.Generic;
using TurboMode.Patches;
using System.Linq;

namespace TurboMode.Tests
{
    public class ResourceContainerGroupCacheTests
    {

        // not the actual id numbers
        static readonly ResourceDefinitionID methane = new(11);
        static readonly ResourceDefinitionID oxygen = new(12);

        (ResourceContainerGroup, ResourceContainerGroupCache) MakeTestGroup()
        {
            ContainedResourceData smallMethaneTankSpec = new()
            {
                ResourceID = methane,
                CapacityUnits = 800,
                StoredUnits = 700,
            };
            ContainedResourceData smallOxTankSpec = new()
            {
                ResourceID = oxygen,
                CapacityUnits = 320,
                StoredUnits = 200,
            };

            ContainedResourceData largeMethaneTankSpec = new()
            {
                ResourceID = methane,
                CapacityUnits = 5120,
                StoredUnits = 4000,
            };
            ContainedResourceData largeOxTankSpec = new()
            {
                ResourceID = oxygen,
                CapacityUnits = 2050,
                StoredUnits = 180,
            };

            var smallContainer = new ResourceContainer(smallMethaneTankSpec, smallOxTankSpec);
            var largeContainer = new ResourceContainer(largeMethaneTankSpec, largeOxTankSpec);

            var group = new ResourceContainerGroup(smallContainer, largeContainer);
            return (group, new ResourceContainerGroupCache(group));
        }

        void ValidateCacheAmountsMatch(ResourceContainerGroup group, ResourceContainerGroupCache cache)
        {
            Assert.AreEqual(group.GetResourceCapacityUnits(methane), cache.GetResourceCapacityUnits(methane));
            Assert.AreEqual(group.GetResourceCapacityUnits(oxygen), cache.GetResourceCapacityUnits(oxygen));

            Assert.AreEqual(group.GetResourceStoredUnits(methane), cache.GetResourceStoredUnits(methane));
            Assert.AreEqual(group.GetResourceStoredUnits(oxygen), cache.GetResourceStoredUnits(oxygen));

            Assert.AreEqual(group.GetResourcePreProcessedUnits(methane), cache.GetResourcePreProcessedUnits(methane));
            Assert.AreEqual(group.GetResourcePreProcessedUnits(oxygen), cache.GetResourcePreProcessedUnits(oxygen));

            Assert.AreEqual(group.GetResourceEmptyUnits(methane), cache.GetResourceEmptyUnits(methane, false));
            Assert.AreEqual(group.GetResourceEmptyUnits(oxygen), cache.GetResourceEmptyUnits(oxygen, false));

            Assert.AreEqual(group.GetResourceEmptyUnits(methane, true), cache.GetResourceEmptyUnits(methane, true));
            Assert.AreEqual(group.GetResourceEmptyUnits(oxygen, true), cache.GetResourceEmptyUnits(oxygen, true));
        }

        void ValidateSyncChangesNothing(ResourceContainerGroup group, ResourceContainerGroupCache cache)
        {
            HashSet<FlowRequests.ContainerResourceChangedNote> containersChanged = new();
            var copy = new ResourceContainerGroup(group, true);

            // Their deep copy does not copy preprocessed, but that's something I want to see if changed.
            for (int i = 0; i < group._containers.Count; i++)
            {
                var container = group._containers[i];
                var containerCopy = copy._containers[i];

                for (int j = 0; j < container._resourceIDMap.Count; j++)
                {
                    containerCopy._preprocessedUnitsLookup[j] = container._preprocessedUnitsLookup[j];
                }
            }

            cache.SyncToGroup(containersChanged);

            Assert.That(containersChanged.Count.Equals(0));

            for (int i = 0; i < group._containers.Count; i++)
            {
                var container = group._containers[i];
                var containerCopy = copy._containers[i];

                for (int j = 0; j < container._resourceIDMap.Count; j++)
                {
                    Assert.AreEqual(containerCopy._resourceIDMap[j], container._resourceIDMap[j]);
                    Assert.AreEqual(containerCopy._capacityUnitsLookup[j], container._capacityUnitsLookup[j]);
                    Assert.AreEqual(containerCopy._storedUnitsLookup[j], container._storedUnitsLookup[j]);
                    Assert.AreEqual(containerCopy._preprocessedUnitsLookup[j], container._preprocessedUnitsLookup[j]);
                }
            }
        }

        [Test]
        public void StoresCorrectAmounts()
        {
            var (group, cache) = MakeTestGroup();

            cache.SyncFromGroup();

            ValidateCacheAmountsMatch(group, cache);
            ValidateSyncChangesNothing(group, cache);
        }

        [Test]
        public void AddsCorrectAmounts()
        {
            var (group, cache) = MakeTestGroup();

            cache.SyncFromGroup();

            var rgMethaneAdded = group.AddResourceUnits(methane, 10);
            var rgcMethaneAdded = cache.AddResourceUnits(methane, 10);
            var rgOxAdded = group.AddResourceUnits(oxygen, 20);
            var rgcOxAdded = cache.AddResourceUnits(oxygen, 20);

            Assert.AreEqual(rgMethaneAdded, rgcMethaneAdded);
            Assert.AreEqual(rgOxAdded, rgcOxAdded);

            ValidateCacheAmountsMatch(group, cache);
            ValidateSyncChangesNothing(group, cache);
        }

        [Test]
        public void AddsCorrectAmountsClampedByCapacity()
        {
            var (group, cache) = MakeTestGroup();

            cache.SyncFromGroup();

            Assert.AreEqual(
                group.AddResourceUnits(methane, 9001),
                cache.AddResourceUnits(methane, 9001)
            );
            ValidateCacheAmountsMatch(group, cache);

            Assert.AreEqual(
                group.AddResourceUnits(oxygen, 9001),
                cache.AddResourceUnits(oxygen, 9001)
            );
            ValidateCacheAmountsMatch(group, cache);

            ValidateSyncChangesNothing(group, cache);
        }

        [Test]
        public void RemovesCorrectAmounts()
        {
            var (group, cache) = MakeTestGroup();

            cache.SyncFromGroup();

            var rgMethaneRemoved = group.RemoveResourceUnits(methane, 10);
            var rgcMethaneRemoved = cache.RemoveResourceUnits(methane, 10);
            var rgOxRemoved = group.RemoveResourceUnits(oxygen, 20);
            var rgcOxRemoved = cache.RemoveResourceUnits(oxygen, 20);

            Assert.AreEqual(rgMethaneRemoved, rgcMethaneRemoved);
            Assert.AreEqual(rgOxRemoved, rgcOxRemoved);

            ValidateCacheAmountsMatch(group, cache);
            ValidateSyncChangesNothing(group, cache);
        }

        [Test]
        public void RemovesCorrectAmountsClampedByZero()
        {
            var (group, cache) = MakeTestGroup();

            cache.SyncFromGroup();

            Assert.AreEqual(
                group.RemoveResourceUnits(methane, 9001),
                cache.RemoveResourceUnits(methane, 9001)
            );
            ValidateCacheAmountsMatch(group, cache);

            Assert.AreEqual(
                group.RemoveResourceUnits(oxygen, 9001),
                cache.RemoveResourceUnits(oxygen, 9001)
            );
            ValidateCacheAmountsMatch(group, cache);

            ValidateSyncChangesNothing(group, cache);
        }

        [Test]
        public void FillsToCapacity()
        {
            var (group, cache) = MakeTestGroup();

            cache.SyncFromGroup();

            Assert.AreEqual(
                group.FillResourceToCapacity(methane),
                cache.FillResourceToCapacity(methane)
            );
            ValidateCacheAmountsMatch(group, cache);

            Assert.AreEqual(
                group.FillResourceToCapacity(oxygen),
                cache.FillResourceToCapacity(oxygen)
            );
            ValidateCacheAmountsMatch(group, cache);

            ValidateSyncChangesNothing(group, cache);
        }

        [Test]
        public void ConsumesPreprocced()
        {
            var (group, cache) = MakeTestGroup();

            cache.SyncFromGroup();

            Assert.AreEqual(
                group.ConsumePreProcessedResourceUnits(methane, 15),
                cache.ConsumePreProcessedResourceUnits(methane, 15)
            );
            ValidateCacheAmountsMatch(group, cache);

            Assert.AreEqual(
                group.ConsumePreProcessedResourceUnits(oxygen, 30),
                cache.ConsumePreProcessedResourceUnits(oxygen, 30)
            );
            ValidateCacheAmountsMatch(group, cache);

            ValidateSyncChangesNothing(group, cache);
        }

        [Test]
        public void ConsumesPreproccedClamped()
        {
            var (group, cache) = MakeTestGroup();

            cache.SyncFromGroup();

            Assert.AreEqual(
                group.ConsumePreProcessedResourceUnits(methane, 9001),
                cache.ConsumePreProcessedResourceUnits(methane, 9001)
            );
            ValidateCacheAmountsMatch(group, cache);

            Assert.AreEqual(
                group.ConsumePreProcessedResourceUnits(oxygen, 9001),
                cache.ConsumePreProcessedResourceUnits(oxygen, 9001)
            );
            ValidateCacheAmountsMatch(group, cache);

            ValidateSyncChangesNothing(group, cache);
        }

        [Test]
        public void StoresPreprocced()
        {
            var (group, cache) = MakeTestGroup();

            cache.SyncFromGroup();

            Assert.AreEqual(
                group.StorePreProcessedResourceUnits(methane, 10),
                cache.StorePreProcessedResourceUnits(methane, 10)
            );
            ValidateCacheAmountsMatch(group, cache);

            Assert.AreEqual(
                group.StorePreProcessedResourceUnits(oxygen, 20),
                cache.StorePreProcessedResourceUnits(oxygen, 20)
            );
            ValidateCacheAmountsMatch(group, cache);

            ValidateSyncChangesNothing(group, cache);
        }
        [Test]
        public void StoresClamped()
        {
            var (group, cache) = MakeTestGroup();

            cache.SyncFromGroup();

            Assert.AreEqual(
                group.StorePreProcessedResourceUnits(methane, 9001),
                cache.StorePreProcessedResourceUnits(methane, 9001)
            );
            ValidateCacheAmountsMatch(group, cache);

            Assert.AreEqual(
                group.StorePreProcessedResourceUnits(oxygen, 9001),
                cache.StorePreProcessedResourceUnits(oxygen, 9001)
            );
            ValidateCacheAmountsMatch(group, cache);

            ValidateSyncChangesNothing(group, cache);
        }


        [Test]
        public void DumpsPreprocced()
        {
            var (group, cache) = MakeTestGroup();

            cache.SyncFromGroup();

            Assert.AreEqual(
                group.StorePreProcessedResourceUnits(methane, 10),
                cache.StorePreProcessedResourceUnits(methane, 10)
            );
            ValidateCacheAmountsMatch(group, cache);

            Assert.AreEqual(
                group.StorePreProcessedResourceUnits(oxygen, 9001),
                cache.StorePreProcessedResourceUnits(oxygen, 9001)
            );
            ValidateCacheAmountsMatch(group, cache);

            Assert.AreEqual(
                group.ConsumePreProcessedResourceUnits(methane, 15),
                cache.ConsumePreProcessedResourceUnits(methane, 15)
            );
            ValidateCacheAmountsMatch(group, cache);

            Assert.AreEqual(
                group.ConsumePreProcessedResourceUnits(oxygen, 30),
                cache.ConsumePreProcessedResourceUnits(oxygen, 30)
            );
            ValidateCacheAmountsMatch(group, cache);

            Assert.AreEqual(
                group.DumpPreProcessedResource(methane),
                cache.DumpPreProcessedResource(methane)
            );
            ValidateCacheAmountsMatch(group, cache);

            Assert.AreEqual(
                group.DumpPreProcessedResource(oxygen),
                cache.DumpPreProcessedResource(oxygen)
            );
            ValidateCacheAmountsMatch(group, cache);

            ValidateSyncChangesNothing(group, cache);
        }

        [Test]
        public void ResetsPreprocessed()
        {
            var (group, cache) = MakeTestGroup();

            cache.SyncFromGroup();

            Assert.AreEqual(
                group.StorePreProcessedResourceUnits(methane, 15),
                cache.StorePreProcessedResourceUnits(methane, 15)
            );
            ValidateCacheAmountsMatch(group, cache);

            Assert.AreEqual(
                group.ConsumePreProcessedResourceUnits(oxygen, 30),
                cache.ConsumePreProcessedResourceUnits(oxygen, 30)
            );
            ValidateCacheAmountsMatch(group, cache);

            group.ResetPreProcessedResources();
            cache.ResetPreProcessedResources();
            ValidateCacheAmountsMatch(group, cache);

            ValidateSyncChangesNothing(group, cache);
        }
    }
}