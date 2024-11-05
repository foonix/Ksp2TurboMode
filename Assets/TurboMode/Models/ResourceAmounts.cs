using KSP.Sim.ResourceSystem;
using Unity.Mathematics;

namespace TurboMode.Models
{
    /// <summary>
    /// Replacement for ResourceContainer, except that it can only store one resource type and can be used in Burst code.
    /// </summary>
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

        public readonly double GetResourceEmptyUnits(bool includePreprocessed = false)
            => includePreprocessed ? capacity - stored + preProcessed : capacity - stored;
        public double DumpPreProcessedResource()
        {
            double num = 0.0;
            if (preProcessed < 0.0)
            {
                num = math.abs(preProcessed);
                preProcessed = 0.0;
            }

            return num + stored;
        }

        public double AddResourceUnits(double totalUnitsToAdd)
        {
            double num = capacity;
            double num2 = stored;
            double num3 = num - num2;

            if (num3 <= totalUnitsToAdd)
            {
                stored = num;
                return num3;
            }

            totalUnitsToAdd = math.abs(totalUnitsToAdd);
            stored += totalUnitsToAdd;
            return totalUnitsToAdd;
        }

        public double FillResourceToCapacity()
        {
            double filled = capacity - stored;
            stored = capacity;
            return filled;
        }

        public double ConsumePreProcessedResourceUnits(double totalUnitsToConsume)
        {
            double num = stored;
            double num2 = preProcessed;
            num -= num2;
            if (num <= totalUnitsToConsume)
            {
                preProcessed += num;
                preProcessed -= stored;
                return num;
            }

            totalUnitsToConsume = math.abs(totalUnitsToConsume);
            preProcessed += totalUnitsToConsume;
            preProcessed -= stored;
            //IGAssert.IsTrue(_preprocessedUnitsLookup[dataIndexFromID] <= _capacityUnitsLookup[dataIndexFromID], "Remove Preprocessed Resource total should always be <= capacity");
            return totalUnitsToConsume;
        }
        public double FillPreProcessedResourceToCapacity()
        {
            double num = capacity - stored;
            num = ((!(preProcessed < 0.0))
                ? (num + preProcessed)
                : (num - math.abs(preProcessed)));
            preProcessed = 0.0 - (capacity - stored);
            //IGAssert.IsTrue(math.abs(_preprocessedUnitsLookup[dataIndexFromID]) <= _capacityUnitsLookup[dataIndexFromID] - _storedUnitsLookup[dataIndexFromID], "Filling Preprocessed amount cannot be more than capacity.");
            return num;
        }
        public double StorePreProcessedResourceUnits(double totalUnitsToStore)
        {
            double num = capacity;
            double num2 = stored;
            double num3 = preProcessed;
            double num4 = num - num2;
            num4 = ((!(num3 < 0.0)) ? (num4 + num3) : (num4 - num3));
            if (num4 <= totalUnitsToStore)
            {
                preProcessed = 0.0 - num4;
                return num4;
            }

            totalUnitsToStore = math.abs(totalUnitsToStore);
            preProcessed -= totalUnitsToStore;
            //IGAssert.IsTrue(math.abs(_preprocessedUnitsLookup[dataIndexFromID]) <= num, "Adding Preprocessed Resource units cannot be over capacity.");
            return totalUnitsToStore;
        }
        public double RemoveResourceUnits(double totalUnitsToRemove)
        {
            double num = stored;
            if (num <= totalUnitsToRemove)
            {
                stored = 0.0;
                //InternalPublishContainerChangedMessage(resourceID);
                return num;
            }

            totalUnitsToRemove = math.abs(totalUnitsToRemove);
            stored -= totalUnitsToRemove;
            //InternalPublishContainerChangedMessage(resourceID);
            return totalUnitsToRemove;
        }

        public void ResetPreProcessedResources()
        {
            preProcessed = 0;
        }
    }
}