using KSP.Game;
using KSP.Sim.ResourceSystem;
using MonoMod.RuntimeDetour;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Profiling;
using UnityEngine;
using static KSP.Sim.ResourceSystem.ResourceFlowRequestManager;


namespace TurboMode.Patches
{
    /// <summary>
    /// Conventional optimizations for resource flow request processing.
    ///  - Reduce enumerator garbage from enumerating interfaces like IReadOnlyList<T>
    ///  - Combine multiple passes over the container list where possible,
    ///    hopefully making better use of CPU cache when querying multiple values from the same container.
    /// </summary>
    public class FlowRequests
    {
        private readonly ResourceFlowRequestManager rfrm;
        private readonly HashSet<ContainerResourceChangedNote> containersChanged = new();

        private static readonly ReflectionUtil.EventHelper<ResourceFlowRequestManager, Action> requestsUpdatedHelper
            = new(nameof(ResourceFlowRequestManager.RequestsUpdated));

        // The field is added during prepatch, but I don't have a way to compile against it (yet)
        // So slum it with field accessor for now.
        private static readonly ReflectionUtil.FieldHelper<ResourceFlowRequestManager, FlowRequests> resourceFlowDataField
            = new(typeof(ResourceFlowRequestManager).GetField("turboModeFlowRequestData"));

        private static readonly ProfilerMarker updateFlowRequestsMarker = new("TM FlowRequests.UpdateFlowRequests()");
        private static readonly ProfilerMarker singleRequestMarker = new("TM FlowRequests.ProcessActiveRequests() (single)");
        private static readonly ProfilerMarker requestsUpdatedMarker = new("TM FlowRequests RequestsUpdated (event)");
        private static readonly ProfilerMarker containerChangedMarker = new("TM FlowRequests ContainerChanged (Message)");

        public static List<IDetour> MakeHooks() => new()
        {
            new Hook(
                typeof(ResourceFlowRequestManager).GetMethod("UpdateFlowRequests"),
                (Action<Action<ResourceFlowRequestManager, double, double>, ResourceFlowRequestManager, double, double>)UpdateFlowRequests
                ),
        };

        private FlowRequests(ResourceFlowRequestManager rfrm)
        {
            this.rfrm = rfrm;
        }

        static void UpdateFlowRequests(
            Action<ResourceFlowRequestManager, double, double> orig,
            ResourceFlowRequestManager rfrm,
            double tickUniversalTime, double tickDeltaTime)
        {
            using var marker = updateFlowRequestsMarker.Auto();
            var data = resourceFlowDataField.Get(rfrm);
            if (data is null)
            {
                data = new FlowRequests(rfrm);
                resourceFlowDataField.Set(rfrm, data);
            }
            data.UpdateFlowRequests(tickUniversalTime, tickDeltaTime);
        }

        #region container change message collation
        struct ContainerResourceChangedNote : IEquatable<ContainerResourceChangedNote>
        {
            public ResourceDefinitionID resourceId;
            public ResourceContainer container;

            public readonly bool Equals(ContainerResourceChangedNote other)
                => resourceId == other.resourceId && container == other.container;

            public override readonly int GetHashCode()
                => HashCode.Combine(resourceId, container);
        }

        void MarkContainerChanged(ResourceContainer container, ResourceDefinitionID resourceId)
        {
            // Possibly could save the original value to avoid calling
            // if net level change is zero.  E.G., a full battery loses a small amount of EC
            // just to be immediately filled again in the same update.
            var note = new ContainerResourceChangedNote()
            {
                container = container,
                resourceId = resourceId,
            };
            containersChanged.Add(note);
        }

        void SendContainersChangedMessages()
        {
            using var marker = containerChangedMarker.Auto();
            foreach (var note in containersChanged)
            {
                note.container.InternalPublishContainerChangedMessage(note.resourceId);
            }
            containersChanged.Clear();
        }
        #endregion

        #region ResourceFlowRequestManager "methods"
        void UpdateFlowRequests(double tickUniversalTime, double tickDeltaTime)
        {
            var sessionManager = GameManager.Instance.Game.SessionManager;
            rfrm._infiniteFuelEnabled = sessionManager.IsDifficultyOptionEnabled("InfiniteFuel");
            rfrm._infiniteECEnabled = sessionManager.IsDifficultyOptionEnabled("InfinitePower");

            rfrm._orderedRequests.Clear();
            foreach (ResourceFlowRequestHandle activeRequest in rfrm._activeRequests)
            {
                if (rfrm.GetRequestWrapperInternal(activeRequest, out ManagedRequestWrapper wrapper))
                {
                    rfrm._orderedRequests.Add(wrapper);
                }
            }
            if (rfrm._orderedRequests.Count > 1)
            {
                rfrm._orderedRequests.Sort(s_requestWrapperComparison);
            }
            foreach (ManagedRequestWrapper orderedRequest in rfrm._orderedRequests)
            {
                foreach (FlowInstructionConfig instruction in orderedRequest.instructions)
                {
                    ResourceFlowPriorityQuerySolver setSolver = rfrm.GetSetSolver(instruction);
                    instruction.SearchPriorityGroup = setSolver.QueryFlowModePriorities(instruction.FlowTarget, instruction.FlowDirection, instruction.FlowMode);
                    rfrm.CreateRequestContainerGroup(instruction);
                }
            }
            ProcessActiveRequests(rfrm, rfrm._orderedRequests, tickUniversalTime, tickDeltaTime);

            SendContainersChangedMessages();

            if (rfrm._orderedRequests.Count > 0)
            {
                using var requestsUpdated = requestsUpdatedMarker.Auto();
                requestsUpdatedHelper.Get(rfrm)?.Invoke();
            }
        }

        // changes: for -> foreach
        private void ProcessActiveRequests(ResourceFlowRequestManager rfrm,
            List<ManagedRequestWrapper> orderedRequests,
            double tickUniversalTime, double tickDeltaTime)
        {
            foreach (ManagedRequestWrapper managedRequestWrapper in orderedRequests)
            {
                using var marker = singleRequestMarker.Auto();

                managedRequestWrapper.RequestResolutionState.LastTickUniversalTime = tickUniversalTime;
                managedRequestWrapper.RequestResolutionState.LastTickDeltaTime = tickDeltaTime;
                bool fullyFulfilled = true;
                bool partiallyFilled = true;
                float percentageToMove = 1f;
                rfrm._failedResources.Clear();
                foreach (FlowInstructionConfig flowInstructionConfig in managedRequestWrapper.instructions)
                {
                    if (flowInstructionConfig.FlowUnitsTarget < 0.0)
                    {
                        fullyFulfilled = false;
                        partiallyFilled = false;
                        continue;
                    }

                    double ratePerTick = (flowInstructionConfig.FlowUpdateMode == RequestFlowUpdateMode.FLOW_UNITS_PER_SECOND) ? tickDeltaTime : 1.0;

                    double minPerUpdate = flowInstructionConfig.FlowUnitsMinimum * ratePerTick;
                    double maxPerUpdate = flowInstructionConfig.FlowUnitsOptimal * ratePerTick;
                    switch (flowInstructionConfig.FlowDirection)
                    {
                        case FlowDirection.FLOW_INBOUND:
                            {
                                float availableCapacity = (float)GetResourceCapacityUnits(flowInstructionConfig.ResourceContainerGroup, flowInstructionConfig.FlowResource)
                                    - (float)GetResourceStoredUnits(flowInstructionConfig.ResourceContainerGroup, flowInstructionConfig.FlowResource, true);

                                if (maxPerUpdate > (double)availableCapacity)
                                {
                                    fullyFulfilled = false;
                                    if (minPerUpdate > (double)availableCapacity)
                                    {
                                        partiallyFilled = false;
                                        percentageToMove = 0f;
                                    }

                                    rfrm._failedResources.Add(flowInstructionConfig.FlowResource);
                                }

                                if (fullyFulfilled)
                                {
                                    StorePreProcessedResourceUnits(flowInstructionConfig.ResourceContainerGroup, flowInstructionConfig.FlowResource, maxPerUpdate);
                                }
                                else if (partiallyFilled)
                                {
                                    float b2 = 0f;
                                    if (availableCapacity > 0)
                                    {
                                        b2 = availableCapacity / (float)maxPerUpdate;
                                    }

                                    percentageToMove = Mathf.Min(percentageToMove, b2);
                                    StorePreProcessedResourceUnits(flowInstructionConfig.ResourceContainerGroup, flowInstructionConfig.FlowResource, (float)maxPerUpdate * percentageToMove);
                                }

                                break;
                            }
                        case FlowDirection.FLOW_OUTBOUND:
                            {
                                float storedUnits = (float)GetResourceStoredUnits(flowInstructionConfig.ResourceContainerGroup, flowInstructionConfig.FlowResource, true);
                                if (maxPerUpdate > (double)storedUnits)
                                {
                                    fullyFulfilled = false;
                                    if (minPerUpdate > (double)storedUnits)
                                    {
                                        partiallyFilled = false;
                                        percentageToMove = 0f;
                                    }

                                    rfrm._failedResources.Add(flowInstructionConfig.FlowResource);
                                }

                                if (fullyFulfilled)
                                {
                                    ConsumePreProcessedResourceUnits(flowInstructionConfig.ResourceContainerGroup, flowInstructionConfig.FlowResource, maxPerUpdate);
                                }
                                else if (partiallyFilled)
                                {
                                    float b = 0f;
                                    if (storedUnits > 0f)
                                    {
                                        b = storedUnits / (float)maxPerUpdate;
                                    }

                                    percentageToMove = Mathf.Min(percentageToMove, b);
                                    ConsumePreProcessedResourceUnits(flowInstructionConfig.ResourceContainerGroup, flowInstructionConfig.FlowResource, (float)maxPerUpdate * percentageToMove);
                                }

                                break;
                            }
                    }
                }

                if (fullyFulfilled || partiallyFilled)
                {
                    foreach (FlowInstructionConfig instruction in managedRequestWrapper.instructions)
                    {
                        double num6 = instruction.FlowUnitsOptimal * ((instruction.FlowUpdateMode == RequestFlowUpdateMode.FLOW_UNITS_PER_SECOND) ? tickDeltaTime : 1.0) * (double)percentageToMove;
                        if (instruction.FlowUnitsTarget > 0.0)
                        {
                            instruction.FlowUnitsTarget -= num6;
                            if (instruction.FlowUnitsTarget < 0.0)
                            {
                                num6 += instruction.FlowUnitsTarget;
                            }
                        }

                        switch (instruction.FlowDirection)
                        {
                            case FlowDirection.FLOW_INBOUND:
                                AddResourceUnits(instruction.ResourceContainerGroup, instruction.FlowResource, num6);
                                break;
                            case FlowDirection.FLOW_OUTBOUND:
                                RemoveResourceUnits(instruction.ResourceContainerGroup, instruction.FlowResource, num6);
                                break;
                        }

                        ResetPreProcessedResources(instruction.ResourceContainerGroup);
                    }

                    percentageToMove = Mathf.Clamp01(percentageToMove);
                    double num7 = percentageToMove;
                    if (double.IsNaN(num7))
                    {
                        num7 = 0.0;
                    }

                    managedRequestWrapper.UpdateStateDeliveryAccepted(tickUniversalTime, tickDeltaTime, num7);
                }
                else
                {
                    UpdateStateDeliveryRejected(managedRequestWrapper, tickUniversalTime, tickDeltaTime, rfrm._failedResources);
                }
            }
        }
        #endregion

        #region ResourceContainerGroupSequence "methods"
        // changed:
        // avoid interface enumerator allocations
        // unpack loops in stack
        double GetResourceCapacityUnits(ResourceContainerGroupSequence rcgs, ResourceDefinitionID resourceID)
        {
            double total = 0.0;
            foreach (ResourceContainerGroup group in rcgs._groupsInSequence)
            {
                foreach (ResourceContainer container in group._containers)
                {
                    total += GetContainedValue(container, resourceID, container._capacityUnitsLookup);
                }
            }

            return total;
        }

        // changed:
        // avoid interface enumerator allocation
        // unpack loops in stack
        double GetResourceStoredUnits(ResourceContainerGroupSequence rcgs, ResourceDefinitionID resourceID, bool includePreProcessed)
        {
            double total = 0.0;
            foreach (ResourceContainerGroup group in rcgs._groupsInSequence)
            {
                foreach (ResourceContainer container in group._containers)
                {
                    total += GetContainedValue(container, resourceID, container._storedUnitsLookup);
                }

                if (includePreProcessed)
                {
                    foreach (ResourceContainer container in group._containers)
                    {
                        total -= GetContainedValue(container, resourceID, container._preprocessedUnitsLookup);
                    }
                }
            }

            return total;
        }

        // changed:
        // for -> foreach
        double AddResourceUnits(ResourceContainerGroupSequence rcgs, ResourceDefinitionID resourceID, double totalUnitsToAdd)
        {
            double remaining = totalUnitsToAdd;
            foreach (var group in rcgs._groupsInSequence)
            {
                remaining -= AddResourceUnits(group, resourceID, remaining);
            }

            return totalUnitsToAdd - remaining;
        }

        double StorePreProcessedResourceUnits(ResourceContainerGroupSequence rcgs, ResourceDefinitionID resourceID, double totalUnitsToStore)
        {
            double remaining = totalUnitsToStore;
            foreach (var group in rcgs._groupsInSequence)
            {
                remaining -= StorePreProcessedResourceUnits(group, resourceID, remaining);
            }

            return totalUnitsToStore - remaining;
        }

        // changed:
        // avoid interface enumerator allocation
        // unpack loops in stack
        void ResetPreProcessedResources(ResourceContainerGroupSequence rcgs)
        {
            foreach (ResourceContainerGroup group in rcgs._groupsInSequence)
            {
                foreach (ResourceContainer container in group._containers)
                {
                    container.ResetPreProcessedResources();
                }
            }
        }

        // changed:
        // for -> foreach
        // Avoid interface enumerator allocation that would create.
        double ConsumePreProcessedResourceUnits(ResourceContainerGroupSequence rcgs, ResourceDefinitionID resourceID, double totalUnitsToConsume)
        {
            double remaining = totalUnitsToConsume;
            foreach (ResourceContainerGroup group in rcgs._groupsInSequence)
            {
                if (!(remaining > 0.0))
                {
                    break;
                }

                remaining -= ConsumePreProcessedResourceUnits(group, resourceID, remaining);
            }

            return totalUnitsToConsume - remaining;
        }

        // changed:
        // for -> foreach
        // Avoid interface enumerator allocation that would create.
        double RemoveResourceUnits(ResourceContainerGroupSequence rcgs, ResourceDefinitionID resourceID, double totalUnitsToRemove)
        {
            double remaining = totalUnitsToRemove;
            foreach (ResourceContainerGroup group in rcgs._groupsInSequence)
            {
                if (!(remaining > 0.0))
                {
                    break;
                }

                remaining -= RemoveResourceUnits(group, resourceID, remaining);
            }

            return totalUnitsToRemove - remaining;
        }
        #endregion

        #region ResourceContainerGroup "methods"
        // changed:
        // for -> foreach
        // Avoid interface enumerator allocation that would create.
        double RemoveResourceUnits(ResourceContainerGroup rcg, ResourceDefinitionID resourceID, double totalUnitsToRemove)
        {
            if (totalUnitsToRemove == 0.0)
            {
                return 0.0;
            }

            double resourceStoredUnits = GetResourceStoredUnits(rcg, resourceID);
            if (resourceStoredUnits <= totalUnitsToRemove)
            {
                rcg.DumpResource(resourceID);
                return resourceStoredUnits;
            }

            double num = totalUnitsToRemove / resourceStoredUnits;
            foreach (ResourceContainer container in rcg._containers)
            {
                double resourceStoredUnits2 = GetContainedValue(container, resourceID, container._storedUnitsLookup);
                double totalUnitsToRemove2 = num * resourceStoredUnits2;
                RemoveResourceUnits(container, resourceID, totalUnitsToRemove2);
            }

            return totalUnitsToRemove;
        }


        // changed:
        // for -> foreach
        // Avoid interface enumerator allocation that would create.
        double GetResourceStoredUnits(ResourceContainerGroup rcg, ResourceDefinitionID resourceID)
        {
            double stored = 0.0;
            foreach (ResourceContainer container in rcg._containers)
            {
                stored += GetContainedValue(container, resourceID, container._storedUnitsLookup);
            }

            return stored;
        }

        double AddResourceUnits(ResourceContainerGroup rcg, ResourceDefinitionID resourceId, double totalUnitsToAdd)
        {
            if (totalUnitsToAdd == 0.0)
            {
                return 0.0;
            }

            double resourceEmptyUnits = 0;
            foreach (ResourceContainer container in rcg._containers)
            {
                resourceEmptyUnits += container.GetResourceEmptyUnits(resourceId, false);
            }

            if (resourceEmptyUnits <= totalUnitsToAdd)
            {
                FillResourceToCapacity(rcg, resourceId);
                return resourceEmptyUnits;
            }

            double num = totalUnitsToAdd / resourceEmptyUnits;
            foreach (ResourceContainer container in rcg._containers)
            {
                double resourceEmptyUnits2 = container.GetResourceEmptyUnits(resourceId, false);
                double totalUnitsToAdd2 = num * resourceEmptyUnits2;
                AddResourceUnits(container, resourceId, totalUnitsToAdd2);
            }

            return totalUnitsToAdd;
        }

        double FillResourceToCapacity(ResourceContainerGroup rcg, ResourceDefinitionID resourceID)
        {
            double added = 0.0;
            foreach (ResourceContainer container in rcg._containers)
            {
                added += container.FillResourceToCapacity(resourceID);
            }

            return added;
        }

        double FillPreProcessedResourceToCapacity(ResourceContainerGroup rcg, ResourceDefinitionID resourceID)
        {
            double filled = 0.0;
            foreach (ResourceContainer container in rcg._containers)
            {
                filled += container.FillPreProcessedResourceToCapacity(resourceID);
            }

            return filled;
        }

        // changed:
        // merge group capacity/stored/preprocess getters into single pass.
        double StorePreProcessedResourceUnits(ResourceContainerGroup rcg, ResourceDefinitionID resourceID, double totalUnitsToStore)
        {
            if (totalUnitsToStore == 0.0)
            {
                return 0.0;
            }

            double num = 0;
            foreach (var container in rcg._containers)
            {
                num += GetContainedValue(container, resourceID, container._capacityUnitsLookup)
                    - GetContainedValue(container, resourceID, container._storedUnitsLookup)
                    + GetContainedValue(container, resourceID, container._preprocessedUnitsLookup);
            }

            if (num <= totalUnitsToStore)
            {
                //return rcg.FillPreProcessedResourceToCapacity(resourceID);
                return FillPreProcessedResourceToCapacity(rcg, resourceID);
            }

            double num2 = totalUnitsToStore / num;
            foreach (ResourceContainer container in rcg._containers)
            {
                double resourceEmptyUnits = container.GetResourceEmptyUnits(resourceID, includePreProcessed: true);
                double totalUnitsToStore2 = num2 * resourceEmptyUnits;
                container.StorePreProcessedResourceUnits(resourceID, totalUnitsToStore2);
            }

            return totalUnitsToStore;
        }

        // changed:
        // for -> foreach
        // Avoid interface enumerator allocation that would create.
        // fetch container preprocess/stored units for the same container in a single pass over the group.
        double ConsumePreProcessedResourceUnits(ResourceContainerGroup rcg, ResourceDefinitionID resourceID, double totalUnitsToConsume)
        {
            if (totalUnitsToConsume == 0.0)
            {
                return 0.0;
            }

            // what to call this variable?
            double resourcePreProcessedUnits = 0;
            foreach (ResourceContainer container in rcg._containers)
            {
                resourcePreProcessedUnits += GetContainedValue(container, resourceID, container._preprocessedUnitsLookup);
                resourcePreProcessedUnits += GetContainedValue(container, resourceID, container._storedUnitsLookup);
            }

            if (resourcePreProcessedUnits <= totalUnitsToConsume)
            {
                return rcg.DumpPreProcessedResource(resourceID);
            }

            double num = totalUnitsToConsume / resourcePreProcessedUnits;
            foreach (ResourceContainer container in rcg._containers)
            {
                double resourcePreProcessedUnits2 = GetContainedValue(container, resourceID, container._preprocessedUnitsLookup);
                resourcePreProcessedUnits2 += GetContainedValue(container, resourceID, container._storedUnitsLookup);

                double totalUnitsToConsume2 = num * resourcePreProcessedUnits2;
                ConsumePreProcessedResourceUnits(container, resourceID, totalUnitsToConsume2);
            }

            return totalUnitsToConsume;
        }
        #endregion

        #region ManagedRequestWrapper "methods"
        // changed:
        // pass failedResources directly to avoid list copy.
        void UpdateStateDeliveryRejected(ManagedRequestWrapper wrapper, double tickUniversalTime, double tickDeltaTime, List<ResourceDefinitionID> failedResources)
        {
            wrapper.RequestResolutionState.LastTickUniversalTime = tickUniversalTime;
            wrapper.RequestResolutionState.LastTickDeltaTime = tickDeltaTime;
            wrapper.RequestResolutionState.WasLastTickDeliveryAccepted = false;
            wrapper.RequestResolutionState.LastTickDeliveryNormalized = 0.0;
            wrapper.RequestResolutionState.ResourcesNotProcessed = failedResources;
        }
        #endregion

        #region ResourceContainer "methods"
        double AddResourceUnits(ResourceContainer container, ResourceDefinitionID resourceID, double totalUnitsToAdd)
        {
            int index = container.GetDataIndexFromID(resourceID);
            if (index == -1)
            {
                return 0.0;
            }
            double capacity = container._capacityUnitsLookup[index];
            double stored = container._storedUnitsLookup[index];
            double freeSpace = capacity - stored;
            if (freeSpace <= totalUnitsToAdd)
            {
                container._storedUnitsLookup[index] = capacity;
                MarkContainerChanged(container, resourceID);
                return freeSpace;
            }
            totalUnitsToAdd = Math.Abs(totalUnitsToAdd);
            container._storedUnitsLookup[index] += totalUnitsToAdd;
            MarkContainerChanged(container, resourceID);
            return totalUnitsToAdd;
        }

        double RemoveResourceUnits(ResourceContainer container, ResourceDefinitionID resourceID, double totalUnitsToRemove)
        {
            int index = container.GetDataIndexFromID(resourceID);
            if (index == -1)
            {
                return 0.0;
            }
            double stored = container._storedUnitsLookup[index];
            if (stored <= totalUnitsToRemove)
            {
                container._storedUnitsLookup[index] = 0.0;
                MarkContainerChanged(container, resourceID);
                return stored;
            }
            totalUnitsToRemove = Math.Abs(totalUnitsToRemove);
            container._storedUnitsLookup[index] -= totalUnitsToRemove;
            MarkContainerChanged(container, resourceID);
            return totalUnitsToRemove;
        }

        public double ConsumePreProcessedResourceUnits(ResourceContainer container, ResourceDefinitionID resourceID, double totalUnitsToConsume)
        {
            int dataIndexFromID = container.GetDataIndexFromID(resourceID);
            if (dataIndexFromID == -1)
            {
                return 0.0;
            }

            double stored = container._storedUnitsLookup[dataIndexFromID];
            double preprocessed = container._preprocessedUnitsLookup[dataIndexFromID];
            var available = stored - preprocessed;
            if (available <= totalUnitsToConsume)
            {
                container._preprocessedUnitsLookup[dataIndexFromID] += available - stored;
                return stored;
            }

            totalUnitsToConsume = Math.Abs(totalUnitsToConsume);
            container._preprocessedUnitsLookup[dataIndexFromID] += totalUnitsToConsume - stored;
            return totalUnitsToConsume;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        double GetContainedValue(ResourceContainer container, ResourceDefinitionID resourceID, List<double> storage)
        {
            int index = container.GetDataIndexFromID(resourceID);

            // This bounds check may not even be necessary.  If the container doesn't have the resource, it might not be in the group.
            // But leaving it here because I can't measure a time difference.
            if (index == -1)
            {
                return 0;
            }

            return storage[index];
        }
        #endregion
    }
}