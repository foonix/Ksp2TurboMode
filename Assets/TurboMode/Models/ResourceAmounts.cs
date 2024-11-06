using KSP.Sim.ResourceSystem;
using Unity.Burst;
using Unity.Mathematics;

namespace TurboMode.Models
{
    /// <summary>
    /// Replacement for ResourceContainer, except that it can only store one resource type and can be used in Burst code.
    /// </summary>
    [BurstCompile]
    public struct ResourceAmounts
    {
        public ResourceDefinitionID resourceId;
        public double capacity;
        public double stored;
        public double preProcessed;

        public ResourceAmounts(ResourceDefinitionID resourceId, double capacity)
        {
            this.resourceId = resourceId;
            this.capacity = capacity;
            stored = 0;
            preProcessed = 0;
        }

        public ResourceAmounts(ContainedResourceData data)
        {
            resourceId = data.ResourceID;
            capacity = data.CapacityUnits;
            stored = data.StoredUnits;
            preProcessed = 0;
        }

        [BurstCompile]
        public readonly double GetResourceEmptyUnits(bool includePreprocessed = false)
            => includePreprocessed ? capacity - stored + preProcessed : capacity - stored;

        [BurstCompile]
        public double DumpPreProcessedResource()
        {
            double dumped = 0.0;

            if (preProcessed < 0.0)
            {
                dumped = math.abs(preProcessed);
                preProcessed = 0.0;
            }

            return dumped + stored;
        }

        [BurstCompile]
        public double AddResourceUnits(double totalUnitsToAdd)
        {
            double emptySpace = capacity - stored;

            if (emptySpace <= totalUnitsToAdd)
            {
                stored = capacity;
                return emptySpace;
            }

            totalUnitsToAdd = math.abs(totalUnitsToAdd);
            stored += totalUnitsToAdd;
            return totalUnitsToAdd;
        }

        [BurstCompile]
        public double FillResourceToCapacity()
        {
            double filled = capacity - stored;
            stored = capacity;
            return filled;
        }

        [BurstCompile]
        public double ConsumePreProcessedResourceUnits(double totalUnitsToConsume)
        {
            double preProcessApplied = stored - preProcessed;
            if (preProcessApplied <= totalUnitsToConsume)
            {
                preProcessed += preProcessApplied - stored;
                return preProcessApplied;
            }

            totalUnitsToConsume = math.abs(totalUnitsToConsume);
            preProcessed += totalUnitsToConsume - stored;
            //IGAssert.IsTrue(_preprocessedUnitsLookup[dataIndexFromID] <= _capacityUnitsLookup[dataIndexFromID], "Remove Preprocessed Resource total should always be <= capacity");
            return totalUnitsToConsume;
        }

        [BurstCompile]
        public double FillPreProcessedResourceToCapacity()
        {
            double available = capacity - stored;
            available = preProcessed >= 0.0
                ? available + preProcessed
                : available - math.abs(preProcessed);
            preProcessed = stored - capacity;
            //IGAssert.IsTrue(math.abs(_preprocessedUnitsLookup[dataIndexFromID]) <= _capacityUnitsLookup[dataIndexFromID] - _storedUnitsLookup[dataIndexFromID], "Filling Preprocessed amount cannot be more than capacity.");
            return available;
        }

        [BurstCompile]
        public double StorePreProcessedResourceUnits(double totalUnitsToStore)
        {
            double available = capacity - stored;
            available = math.abs(available);
            if (available <= totalUnitsToStore)
            {
                preProcessed = -available;
                return available;
            }

            totalUnitsToStore = math.abs(totalUnitsToStore);
            preProcessed -= totalUnitsToStore;
            //IGAssert.IsTrue(math.abs(_preprocessedUnitsLookup[dataIndexFromID]) <= num, "Adding Preprocessed Resource units cannot be over capacity.");
            return totalUnitsToStore;
        }

        [BurstCompile]
        public double RemoveResourceUnits(double totalUnitsToRemove)
        {
            double wasStored = stored;
            if (wasStored <= totalUnitsToRemove)
            {
                stored = 0.0;
                //InternalPublishContainerChangedMessage(resourceID);
                return wasStored;
            }

            totalUnitsToRemove = math.abs(totalUnitsToRemove);
            stored -= totalUnitsToRemove;
            //InternalPublishContainerChangedMessage(resourceID);
            return totalUnitsToRemove;
        }

        [BurstCompile]
        public void ResetPreProcessedResources()
        {
            preProcessed = 0;
        }
    }
}