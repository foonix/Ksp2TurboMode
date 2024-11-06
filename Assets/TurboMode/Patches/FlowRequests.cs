using KSP.Game;
using KSP.Sim.ResourceSystem;
using MonoMod.RuntimeDetour;
using System;
using System.Collections.Generic;
using TurboMode.Models;
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
        private static readonly ReflectionUtil.FieldHelper<ResourceContainerGroup, ResourceContainerGroupCache> resourceContainerGroupCacheField
            = new(typeof(ResourceContainerGroup).GetField("resourceContainerGroupCache"));

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

        ResourceContainerGroupCache GetCache(ResourceContainerGroup rcg)
        {
            var cache = resourceContainerGroupCacheField.Get(rcg);
            if (cache is null)
            {
                cache = new ResourceContainerGroupCache(rcg);
                resourceContainerGroupCacheField.Set(rcg, cache);
            }
            return cache;
        }

        #region container change message collation
        public struct ContainerResourceChangedNote : IEquatable<ContainerResourceChangedNote>
        {
            public ResourceDefinitionID resourceId;
            public ResourceContainer container;

            public readonly bool Equals(ContainerResourceChangedNote other)
                => resourceId == other.resourceId && container == other.container;

            public override readonly int GetHashCode()
                => HashCode.Combine(resourceId, container);
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

            // doing a single broad sync in/out is not quite working out.
            // Something's mucking with container data somewhere and I haven't found what.
            //SyncToGroupCaches();
            ProcessActiveRequests(rfrm._orderedRequests, tickUniversalTime, tickDeltaTime);
            //SyncFromGroupCaches();

            SendContainersChangedMessages();

            if (rfrm._orderedRequests.Count > 0)
            {
                using var requestsUpdated = requestsUpdatedMarker.Auto();
                requestsUpdatedHelper.Get(rfrm)?.Invoke();
            }
        }

        private void ProcessActiveRequests(List<ManagedRequestWrapper> orderedRequests,
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
                    double optimialPerUpdate = flowInstructionConfig.FlowUnitsOptimal * ratePerTick;

                    SyncToGroupCaches(flowInstructionConfig.ResourceContainerGroup);

                    switch (flowInstructionConfig.FlowDirection)
                    {
                        case FlowDirection.FLOW_INBOUND:
                            {
                                float availableCapacity = (float)GetResourceCapacityUnits(flowInstructionConfig.ResourceContainerGroup, flowInstructionConfig.FlowResource)
                                    - (float)GetResourceStoredUnits(flowInstructionConfig.ResourceContainerGroup, flowInstructionConfig.FlowResource, true);

                                if (optimialPerUpdate > (double)availableCapacity)
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
                                    StorePreProcessedResourceUnits(flowInstructionConfig.ResourceContainerGroup, flowInstructionConfig.FlowResource, optimialPerUpdate);
                                }
                                else if (partiallyFilled)
                                {
                                    float b2 = 0f;
                                    if (availableCapacity > 0)
                                    {
                                        b2 = availableCapacity / (float)optimialPerUpdate;
                                    }

                                    percentageToMove = Mathf.Min(percentageToMove, b2);
                                    StorePreProcessedResourceUnits(flowInstructionConfig.ResourceContainerGroup, flowInstructionConfig.FlowResource, (float)optimialPerUpdate * percentageToMove);
                                }

                                break;
                            }
                        case FlowDirection.FLOW_OUTBOUND:
                            {
                                float storedUnits = (float)GetResourceStoredUnits(flowInstructionConfig.ResourceContainerGroup, flowInstructionConfig.FlowResource, true);
                                if (optimialPerUpdate > (double)storedUnits)
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
                                    ConsumePreProcessedResourceUnits(flowInstructionConfig.ResourceContainerGroup, flowInstructionConfig.FlowResource, optimialPerUpdate);
                                }
                                else if (partiallyFilled)
                                {
                                    float b = 0f;
                                    if (storedUnits > 0f)
                                    {
                                        b = storedUnits / (float)optimialPerUpdate;
                                    }

                                    percentageToMove = Mathf.Min(percentageToMove, b);
                                    ConsumePreProcessedResourceUnits(flowInstructionConfig.ResourceContainerGroup, flowInstructionConfig.FlowResource, (float)optimialPerUpdate * percentageToMove);
                                }

                                break;
                            }
                    }

                    SyncFromGroupCaches(flowInstructionConfig.ResourceContainerGroup);
                }

                if (fullyFulfilled || partiallyFilled)
                {
                    foreach (FlowInstructionConfig instruction in managedRequestWrapper.instructions)
                    {
                        SyncToGroupCaches(instruction.ResourceContainerGroup);

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

                        SyncFromGroupCaches(instruction.ResourceContainerGroup);
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
                    managedRequestWrapper.UpdateStateDeliveryRejected(tickUniversalTime, tickDeltaTime, rfrm._failedResources);
                }
            }
        }

        private void SyncToGroupCaches(ResourceContainerGroupSequence sequence)
        {
            foreach (var group in sequence._groupsInSequence)
            {
                var cache = GetCache(group);
                cache.SyncFromGroup();
            }
        }

        private void SyncFromGroupCaches(ResourceContainerGroupSequence sequence)
        {
            foreach (var group in sequence._groupsInSequence)
            {
                var cache = GetCache(group);
                cache.SyncToGroup(containersChanged);
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
                var cache = GetCache(group);
                total += cache.GetResourceCapacityUnits(resourceID);
            }

            return total;
        }

        double GetResourceStoredUnits(ResourceContainerGroupSequence rcgs, ResourceDefinitionID resourceID, bool includePreProcessed)
        {
            double total = 0.0;
            foreach (ResourceContainerGroup group in rcgs._groupsInSequence)
            {
                var cache = GetCache(group);
                total += cache.GetResourceStoredUnits(resourceID);
                if (includePreProcessed)
                {
                    total -= cache.GetResourcePreProcessedUnits(resourceID);
                }
            }

            return total;
        }

        /// <summary>
        /// Walk sequence in reverse order, allocating as much of totalUnitsToAdd as will fit to each group.
        /// </summary>
        double AddResourceUnits(ResourceContainerGroupSequence rcgs, ResourceDefinitionID resourceId, double totalUnitsToAdd)
        {
            double remaining = totalUnitsToAdd;

            int index = rcgs._groupsInSequence.Count - 1;
            while (index >= 0 && remaining > 0.0)
            {
                var cache = GetCache(rcgs._groupsInSequence[index]);
                remaining -= cache.AddResourceUnits(resourceId, remaining);
                index--;
            }
            return totalUnitsToAdd - remaining;
        }

        double StorePreProcessedResourceUnits(ResourceContainerGroupSequence rcgs, ResourceDefinitionID resourceID, double totalUnitsToStore)
        {
            double num = totalUnitsToStore;
            int num2 = rcgs._groupsInSequence.Count - 1;
            while (num2 >= 0 && num > 0.0)
            {
                ResourceContainerGroup group = rcgs._groupsInSequence[num2];
                var cache = GetCache(group);
                num -= cache.StorePreProcessedResourceUnits(resourceID, num);
                num2--;
            }

            return totalUnitsToStore - num;
        }

        // changed:
        // avoid interface enumerator allocation
        // unpack loops in stack
        void ResetPreProcessedResources(ResourceContainerGroupSequence rcgs)
        {
            foreach (ResourceContainerGroup group in rcgs._groupsInSequence)
            {
                var cache = GetCache(group);
                cache.ResetPreProcessedResources();
            }
        }

        double ConsumePreProcessedResourceUnits(ResourceContainerGroupSequence rcgs, ResourceDefinitionID resourceID, double totalUnitsToConsume)
        {
            double remaining = totalUnitsToConsume;
            foreach (ResourceContainerGroup group in rcgs._groupsInSequence)
            {
                if (!(remaining > 0.0))
                {
                    break;
                }

                var cache = GetCache(group);
                remaining -= cache.ConsumePreProcessedResourceUnits(resourceID, remaining);
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
                var cache = GetCache(group);
                remaining -= cache.RemoveResourceUnits(resourceID, remaining);
            }

            return totalUnitsToRemove - remaining;
        }
        #endregion
    }
}