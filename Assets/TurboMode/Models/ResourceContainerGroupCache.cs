using KSP.Sim.ResourceSystem;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using TurboMode.Patches;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace TurboMode.Models
{
    /// <summary>
    /// Implements ResourceContainerGroup helpers, but maintains aggregate data and impliment container operations in Burst.
    /// </summary>
    [BurstCompile]
    public class ResourceContainerGroupCache
    {
        readonly ResourceContainerGroup group;
        readonly NativeData nativeData;

        private readonly struct NativeData
        {
            public readonly NativeList<ResourceAmounts> containerAmounts;
            public readonly NativeHashMap<ResourceDefinitionID, ResourceAmounts> aggregateData;
            public NativeData(NativeList<ResourceAmounts> amounts, NativeHashMap<ResourceDefinitionID, ResourceAmounts> aggregateData)
            {
                this.containerAmounts = amounts;
                this.aggregateData = aggregateData;
            }
            public void Clear()
            {
                containerAmounts.Clear();
                aggregateData.Clear();
            }
            public void Dispose()
            {
                if (containerAmounts.IsCreated) containerAmounts.Dispose();
                if (aggregateData.IsCreated) aggregateData.Dispose();
            }
        }

        #region object lifecycle
        public ResourceContainerGroupCache(ResourceContainerGroup group)
        {
            this.group = group;
            nativeData = new(new(Allocator.Persistent), new(4, Allocator.Persistent));
        }

        public void SyncFromGroup()
        {
            nativeData.Clear();
            foreach (var container in group._containers)
            {
                for (int i = 0; i < container._resourceIDMap.Count; i++)
                {
                    var amounts = new ResourceAmounts()
                    {
                        resourceId = container._resourceIDMap[i],
                        stored = container._storedUnitsLookup[i],
                        preProcessed = container._preprocessedUnitsLookup[i],
                        capacity = container._capacityUnitsLookup[i],
                    };
                    nativeData.containerAmounts.Add(amounts);
                }
            }
            UpdateAggregates(nativeData);
        }

        public void SyncToGroup(HashSet<FlowRequests.ContainerResourceChangedNote> containersChanged)
        {
            int dataIndex = 0;
            int foundStorages = 0;
            foreach (var container in group._containers)
            {
                foundStorages += container._resourceIDMap.Count;
                for (int i = 0; i < container._resourceIDMap.Count; i++)
                {
                    var amounts = nativeData.containerAmounts[dataIndex];

                    if (container._resourceIDMap[i] != amounts.resourceId)
                    {
                        Debug.LogWarning("TM: resource ID mismatch saving cached data");
                    }

                    if (amounts.stored != container._storedUnitsLookup[i])
                    {
                        containersChanged.Add(new()
                        {
                            container = container,
                            resourceId = amounts.resourceId,
                        });
                    }

                    container._resourceIDMap[i] = amounts.resourceId;
                    container._storedUnitsLookup[i] = amounts.stored;
                    container._preprocessedUnitsLookup[i] = amounts.preProcessed;
                    // don't change capacity

                    dataIndex++;
                }
            }
            if (foundStorages != dataIndex)
            {
                Debug.LogWarning($"TM: storage count mismatch");
            }
        }

        public void Dispose()
        {
            nativeData.Dispose();
        }

        ~ResourceContainerGroupCache()
        {
            Dispose();
        }
        #endregion

        #region managed interface -> burst
        public double GetResourceCapacityUnits(ResourceDefinitionID resourceId)
        {
            return GetResourceCapacityUnits(nativeData, resourceId);
        }

        public double GetResourceStoredUnits(ResourceDefinitionID resourceId)
        {
            return GetResourceStoredUnits(nativeData, resourceId);
        }

        public double GetResourceEmptyUnits(ResourceDefinitionID resourceId, bool includePreProcessed)
        {
            return GetResourceEmptyUnits(nativeData, resourceId, includePreProcessed);
        }

        public double GetResourcePreProcessedUnits(ResourceDefinitionID resourceId)
        {
            return GetResourcePreProcessedUnits(nativeData, resourceId);
        }

        public double FillResourceToCapacity(ResourceDefinitionID resourceId)
        {
            return FillResourceToCapacity(nativeData, resourceId);
        }

        public double AddResourceUnits(ResourceDefinitionID resourceId, double totalUnitsToAdd)
        {
            return AddResourceUnits(nativeData, resourceId, totalUnitsToAdd);
        }

        public double StorePreProcessedResourceUnits(ResourceDefinitionID resourceId, double totalUnitsToStore)
        {
            return StorePreProcessedResourceUnits(nativeData, resourceId, totalUnitsToStore);
        }

        public double ConsumePreProcessedResourceUnits(ResourceDefinitionID resourceId, double totalUnitsToConsume)
        {
            return ConsumePreProcessedResourceUnits(nativeData, resourceId, totalUnitsToConsume);
        }

        public double DumpPreProcessedResource(ResourceDefinitionID resourceId)
        {
            return DumpPreProcessedResource(nativeData, resourceId);
        }

        public void ResetPreProcessedResources()
        {
            ResetPreProcessedResources(nativeData);
        }

        public double RemoveResourceUnits(ResourceDefinitionID resourceId, double totalUnitsToConsume)
        {
            return RemoveResourceUnits(nativeData, resourceId, totalUnitsToConsume);
        }
        #endregion


        #region Burst impl


        [BurstCompile]
        static void UpdateAggregates(in NativeData data)
        {
            data.aggregateData.Clear();
            foreach (var amounts in data.containerAmounts)
            {
                if (data.aggregateData.TryGetValue(amounts.resourceId, out var aggAmounts))
                {
                    aggAmounts.stored += amounts.stored;
                    aggAmounts.preProcessed += amounts.preProcessed;
                    aggAmounts.capacity += amounts.capacity;
                    var dict = data.aggregateData;
                    dict[amounts.resourceId] = aggAmounts;
                }
                else
                {
                    data.aggregateData.Add(amounts.resourceId, amounts);
                }
            }
        }

        [BurstCompile]
        static double GetResourceCapacityUnits(in NativeData data, in ResourceDefinitionID resourceId)
        {
            if (data.aggregateData.TryGetValue(resourceId, out var agg))
            {
                return agg.capacity;
            }
            throw new ArgumentException($"TM: Capacity not found for {resourceId}");
        }

        [BurstCompile]
        static double GetResourceStoredUnits(in NativeData data, in ResourceDefinitionID resourceId)
        {
            if (data.aggregateData.TryGetValue(resourceId, out var agg))
            {
                return agg.stored;
            }
            throw new ArgumentException($"TM: Stored not found for {resourceId}");
        }

        [BurstCompile]
        double GetResourceEmptyUnits(in NativeData data, in ResourceDefinitionID resourceId, bool includePreProcessed)
        {
            if (data.aggregateData.TryGetValue(resourceId, out var agg))
            {
                if (includePreProcessed)
                {
                    return agg.capacity - agg.stored + agg.preProcessed;
                }
                else
                {
                    return agg.capacity - agg.stored;
                }
            }

            throw new ArgumentException($"TM: empty units not found for {resourceId}");
        }

        [BurstCompile]
        double GetResourcePreProcessedUnits(in NativeData data, in ResourceDefinitionID resourceId)
        {
            if (data.aggregateData.TryGetValue(resourceId, out var agg))
            {
                return agg.preProcessed;
            }
            throw new ArgumentException($"TM: preProcessed not found for {resourceId}");
        }

        [BurstCompile]
        double AddResourceUnits(in NativeData data, in ResourceDefinitionID resourceId, double totalUnitsToAdd)
        {
            if (totalUnitsToAdd == 0.0)
            {
                return 0.0;
            }

            double groupEmptyUnits = GetResourceEmptyUnits(data, resourceId, false);
            if (groupEmptyUnits <= totalUnitsToAdd)
            {
                FillResourceToCapacity(data, resourceId);
                return groupEmptyUnits;
            }

            double ratioPerContainer = totalUnitsToAdd / groupEmptyUnits;
            for (int i = 0; i < data.containerAmounts.Length; i++)
            {
                ref var amounts = ref data.containerAmounts.ElementAt(i);
                if (amounts.resourceId == resourceId)
                {
                    double containerEmptyUnits = amounts.GetResourceEmptyUnits();
                    double toAdd = ratioPerContainer * containerEmptyUnits;
                    amounts.AddResourceUnits(toAdd);
                }
            }
            // todo: avoid full agg rescan
            UpdateAggregates(data);

            return totalUnitsToAdd;
        }

        [BurstCompile]
        double FillResourceToCapacity(in NativeData data, in ResourceDefinitionID resourceId)
        {
            double total = 0;
            for (int i = 0; i < data.containerAmounts.Length; i++)
            {
                ref var amounts = ref data.containerAmounts.ElementAt(i);
                if (amounts.resourceId == resourceId)
                {
                    total += amounts.FillResourceToCapacity();
                }
            }
            // todo: avoid full agg rescan
            UpdateAggregates(data);
            return total;
        }

        [BurstCompile]
        double StorePreProcessedResourceUnits(in NativeData data, in ResourceDefinitionID resourceId, double totalUnitsToStore)
        {
            if (totalUnitsToStore == 0.0)
            {
                return 0.0;
            }

            double available = GetResourceCapacityUnits(data, resourceId)
                - GetResourceStoredUnits(data, resourceId)
                + GetResourcePreProcessedUnits(data, resourceId);

            if (available <= totalUnitsToStore)
            {
                return FillPreProcessedResourceToCapacity(data, resourceId);
            }

            double ratioPerContainer = totalUnitsToStore / available;
            for (int i = 0; i < data.containerAmounts.Length; i++)
            {
                ref var amounts = ref data.containerAmounts.ElementAt(i);
                if (amounts.resourceId == resourceId)
                {
                    double resourceEmptyUnits = amounts.GetResourceEmptyUnits(true);
                    double totalUnitsToStore2 = ratioPerContainer * resourceEmptyUnits;
                    amounts.StorePreProcessedResourceUnits(totalUnitsToStore2);
                }
            }
            // todo: avoid full agg rescan
            UpdateAggregates(data);
            return totalUnitsToStore;
        }

        [BurstCompile]
        double FillPreProcessedResourceToCapacity(in NativeData data, ResourceDefinitionID resourceId)
        {
            double filled = 0;
            for (int i = 0; i < data.containerAmounts.Length; i++)
            {
                ref var amounts = ref data.containerAmounts.ElementAt(i);
                if (amounts.resourceId == resourceId)
                {
                    filled += amounts.FillPreProcessedResourceToCapacity();
                }
            }
            // todo: avoid full agg rescan
            UpdateAggregates(data);
            return filled;
        }

        [BurstCompile]
        double ConsumePreProcessedResourceUnits(in NativeData data, in ResourceDefinitionID resourceId, double totalUnitsToConsume)
        {
            if (totalUnitsToConsume == 0.0)
            {
                return 0.0;
            }

            double resourcePreProcessedUnits = GetResourcePreProcessedUnits(data, resourceId);
            resourcePreProcessedUnits += GetResourceStoredUnits(data, resourceId);
            if (resourcePreProcessedUnits <= totalUnitsToConsume)
            {
                return DumpPreProcessedResource(data, resourceId);
            }

            double ratioPerContainer = totalUnitsToConsume / resourcePreProcessedUnits;
            for (int i = 0; i < data.containerAmounts.Length; i++)
            {
                ref var amounts = ref data.containerAmounts.ElementAt(i);
                if (amounts.resourceId == resourceId)
                {
                    double resourcePreProcessedUnits2 = amounts.preProcessed;
                    resourcePreProcessedUnits2 += amounts.stored;
                    double totalUnitsToConsume2 = ratioPerContainer * resourcePreProcessedUnits2;
                    amounts.ConsumePreProcessedResourceUnits(totalUnitsToConsume2);
                }
            }
            // todo: avoid full agg rescan
            UpdateAggregates(data);

            return totalUnitsToConsume;
        }

        [BurstCompile]
        double DumpPreProcessedResource(in NativeData data, in ResourceDefinitionID resourceId)
        {
            double total = 0;
            for (int i = 0; i < data.containerAmounts.Length; i++)
            {
                ref var amounts = ref data.containerAmounts.ElementAt(i);

                if (amounts.resourceId == resourceId)
                {
                    total += amounts.DumpPreProcessedResource();
                }
            }
            // todo: avoid full agg rescan
            UpdateAggregates(data);
            return total;
        }

        [BurstCompile]
        double RemoveResourceUnits(in NativeData data, in ResourceDefinitionID resourceId, double totalUnitsToRemove)
        {
            if (totalUnitsToRemove == 0.0)
            {
                return 0.0;
            }

            double resourceStoredUnits = GetResourceStoredUnits(data, resourceId);
            if (resourceStoredUnits <= totalUnitsToRemove)
            {
                DumpResource(data, resourceId);
                return resourceStoredUnits;
            }

            double ratioPerContainer = totalUnitsToRemove / resourceStoredUnits;

            for (int i = 0; i < data.containerAmounts.Length; i++)
            {
                ref var amounts = ref data.containerAmounts.ElementAt(i);

                if (amounts.resourceId == resourceId)
                {
                    double resourceStoredUnits2 = amounts.stored;
                    double totalUnitsToRemove2 = ratioPerContainer * resourceStoredUnits2;
                    amounts.RemoveResourceUnits(totalUnitsToRemove2);

                }
            }
            // todo: avoid full agg rescan
            UpdateAggregates(data);

            return totalUnitsToRemove;
        }

        [BurstCompile]
        void ResetPreProcessedResources(in NativeData data)
        {
            for (int i = 0; i < data.containerAmounts.Length; i++)
            {
                ref var amounts = ref data.containerAmounts.ElementAt(i);
                amounts.preProcessed = 0;
            }
            // todo: avoid full agg rescan
            UpdateAggregates(data);
        }

        [BurstCompile]
        double DumpResource(in NativeData data, in ResourceDefinitionID resourceId)
        {
            double dumped = 0;
            for (int i = 0; i < data.containerAmounts.Length; i++)
            {
                ref var amounts = ref data.containerAmounts.ElementAt(i);

                if (amounts.resourceId == resourceId)
                {
                    dumped += amounts.stored;
                    amounts.stored = 0;
                }
            }
            // todo: avoid full agg rescan
            UpdateAggregates(data);
            return dumped;
        }
        #endregion
    }
}
