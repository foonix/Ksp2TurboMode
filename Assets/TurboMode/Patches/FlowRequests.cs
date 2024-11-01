using KSP.Game;
using KSP.Sim.ResourceSystem;
using MonoMod.RuntimeDetour;
using System;
using System.Collections.Generic;
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
        private static readonly ReflectionUtil.EventHelper<ResourceFlowRequestManager, Action> requestsUpdatedHelper
            = new(nameof(ResourceFlowRequestManager.RequestsUpdated));

        private static readonly ProfilerMarker updateFlowRequestsMarker = new("TM FlowRequests.UpdateFlowRequests");
        private static readonly ProfilerMarker requestsUpdatedMarker = new("TM FlowRequests.RequestsUpdated");

        public static List<IDetour> MakeHooks() => new()
        {
            new Hook(
                typeof(ResourceFlowRequestManager).GetMethod("UpdateFlowRequests"),
                (Action<Action<ResourceFlowRequestManager, double, double>, ResourceFlowRequestManager, double, double>)UpdateFlowRequests
                ),
        };

        #region ResourceFlowRequestManager "methods"
        public static void UpdateFlowRequests(
            Action<ResourceFlowRequestManager, double, double> orig,
            ResourceFlowRequestManager rfrm,
            double tickUniversalTime, double tickDeltaTime)
        {
            using var marker = updateFlowRequestsMarker.Auto();

            //if (GameManager.Instance != null && GameManager.Instance.Game != null && GameManager.Instance.Game.SessionManager != null)
            //{
            //rfrm._infiniteFuelEnabled = GameManager.Instance.Game.SessionManager.IsDifficultyOptionEnabled("InfiniteFuel");
            //rfrm._infiniteECEnabled = GameManager.Instance.Game.SessionManager.IsDifficultyOptionEnabled("InfinitePower");
            var sessionManager = GameManager.Instance.Game.SessionManager;
            rfrm._infiniteFuelEnabled = sessionManager.IsDifficultyOptionEnabled("InfiniteFuel");
            rfrm._infiniteECEnabled = sessionManager.IsDifficultyOptionEnabled("InfinitePower");
            //}
            //else
            //{
            //    rfrm._infiniteFuelEnabled = false;
            //    rfrm._infiniteECEnabled = false;
            //}
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
            //rfrm.ProcessActiveRequests(rfrm._orderedRequests, tickUniversalTime, tickDeltaTime);
            ProcessActiveRequests(rfrm, rfrm._orderedRequests, tickUniversalTime, tickDeltaTime);
            if (rfrm._orderedRequests.Count > 0)
            {
                using var requestsUpdated = requestsUpdatedMarker.Auto();
                requestsUpdatedHelper.Get(rfrm)?.Invoke();
            }
        }

        // changes: for -> foreach
        private static void ProcessActiveRequests(ResourceFlowRequestManager rfrm,
            List<ManagedRequestWrapper> orderedRequests,
            double tickUniversalTime, double tickDeltaTime)
        {
            //for (int i = 0; i < orderedRequests.Count; i++)
            foreach (ManagedRequestWrapper managedRequestWrapper in orderedRequests)
            {
                //ManagedRequestWrapper managedRequestWrapper = orderedRequests[i];
                managedRequestWrapper.RequestResolutionState.LastTickUniversalTime = tickUniversalTime;
                managedRequestWrapper.RequestResolutionState.LastTickDeltaTime = tickDeltaTime;
                bool fullyFulfilled = true;
                bool partiallyFilled = true;
                float percentageToMove = 1f;
                rfrm._failedResources.Clear();
                foreach (FlowInstructionConfig flowInstructionConfig in managedRequestWrapper.instructions)
                //for (int j = 0; j < managedRequestWrapper.instructions.Count; j++)
                {
                    //FlowInstructionConfig flowInstructionConfig = managedRequestWrapper.instructions[j];
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
                                //float availableCapacity = (float)flowInstructionConfig.ResourceContainerGroup.GetResourceCapacityUnits(flowInstructionConfig.FlowResource)
                                //    - (float)flowInstructionConfig.ResourceContainerGroup.GetResourceStoredUnits(flowInstructionConfig.FlowResource, includePreProcessed: true);

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
                                    //flowInstructionConfig.ResourceContainerGroup.StorePreProcessedResourceUnits(flowInstructionConfig.FlowResource, maxPerUpdate);
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
                                    //flowInstructionConfig.ResourceContainerGroup.StorePreProcessedResourceUnits(flowInstructionConfig.FlowResource, (float)maxPerUpdate * percentageToMove);
                                    StorePreProcessedResourceUnits(flowInstructionConfig.ResourceContainerGroup, flowInstructionConfig.FlowResource, (float)maxPerUpdate * percentageToMove);
                                }

                                break;
                            }
                        case FlowDirection.FLOW_OUTBOUND:
                            {
                                //float storedUnits = (float)flowInstructionConfig.ResourceContainerGroup.GetResourceStoredUnits(flowInstructionConfig.FlowResource, includePreProcessed: true);
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
                                    //flowInstructionConfig.ResourceContainerGroup.ConsumePreProcessedResourceUnits(flowInstructionConfig.FlowResource, maxPerUpdate);
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
                                    //flowInstructionConfig.ResourceContainerGroup.ConsumePreProcessedResourceUnits(flowInstructionConfig.FlowResource, (float)maxPerUpdate * unitsToMove);
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
                                //instruction.ResourceContainerGroup.AddResourceUnits(instruction.FlowResource, num6);
                                AddResourceUnits(instruction.ResourceContainerGroup, instruction.FlowResource, num6);
                                break;
                            case FlowDirection.FLOW_OUTBOUND:
                                //instruction.ResourceContainerGroup.RemoveResourceUnits(instruction.FlowResource, num6);
                                RemoveResourceUnits(instruction.ResourceContainerGroup, instruction.FlowResource, num6);
                                break;
                        }

                        //instruction.ResourceContainerGroup.ResetPreProcessedResources();
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
                    managedRequestWrapper.UpdateStateDeliveryRejected(tickUniversalTime, tickDeltaTime, rfrm._failedResources);
                }
            }
        }
        #endregion

        #region ResourceContainerGroupSequence "methods"
        // changed:
        // avoid interface enumerator allocations
        // unpack loops in stack
        static double GetResourceCapacityUnits(ResourceContainerGroupSequence rcgs, ResourceDefinitionID resourceID)
        {
            double total = 0.0;
            foreach (ResourceContainerGroup group in rcgs._groupsInSequence)
            {
                //total += group.GetResourceCapacityUnits(resourceID);
                foreach (ResourceContainer container in group._containers)
                {
                    total += container.GetResourceCapacityUnits(resourceID);
                }
            }

            return total;
        }

        // changed:
        // avoid interface enumerator allocation
        // unpack loops in stack
        static double GetResourceStoredUnits(ResourceContainerGroupSequence rcgs, ResourceDefinitionID resourceID, bool includePreProcessed)
        {
            double total = 0.0;
            foreach (ResourceContainerGroup group in rcgs._groupsInSequence)
            {
                //total += group.GetResourceStoredUnits(resourceID);
                foreach (ResourceContainer container in group._containers)
                {
                    total += container.GetResourceStoredUnits(resourceID);
                }

                if (includePreProcessed)
                {
                    //total -= group.GetResourcePreProcessedUnits(resourceID);
                    foreach (ResourceContainer container in group._containers)
                    {
                        total -= container.GetResourcePreProcessedUnits(resourceID);
                    }
                }
            }

            return total;
        }

        // changed:
        // for -> foreach
        static double AddResourceUnits(ResourceContainerGroupSequence rcgs, ResourceDefinitionID resourceID, double totalUnitsToAdd)
        {
            double remaining = totalUnitsToAdd;
            //int num2 = rcgs.GroupsInSequence.Count - 1;
            //while (num2 >= 0 && remaining > 0.0)
            foreach (var group in rcgs._groupsInSequence)
            {
                //ResourceContainerGroup resourceContainerGroup = rcgs.GroupsInSequence[num2];
                //remaining -= group.AddResourceUnits(resourceID, remaining);
                //num2--;
                remaining -= AddResourceUnits(group, resourceID, remaining);
            }

            return totalUnitsToAdd - remaining;
        }

        static double StorePreProcessedResourceUnits(ResourceContainerGroupSequence rcgs, ResourceDefinitionID resourceID, double totalUnitsToStore)
        {
            double remaining = totalUnitsToStore;
            //int num2 = rcgs.GroupsInSequence.Count - 1;
            //while (num2 >= 0 && num > 0.0)
            foreach (var group in rcgs._groupsInSequence)
            {
                //ResourceContainerGroup resourceContainerGroup = rcgs.GroupsInSequence[num2];
                //remaining -= group.StorePreProcessedResourceUnits(resourceID, remaining);
                //num2--;
                remaining -= StorePreProcessedResourceUnits(group, resourceID, remaining);
            }

            return totalUnitsToStore - remaining;
        }

        // changed:
        // avoid interface enumerator allocation
        // unpack loops in stack
        static void ResetPreProcessedResources(ResourceContainerGroupSequence rcgs)
        {
            foreach (ResourceContainerGroup group in rcgs._groupsInSequence)
            {
                //group.ResetPreProcessedResources();
                foreach (ResourceContainer container in group._containers)
                {
                    container.ResetPreProcessedResources();
                }
            }
        }

        // changed:
        // for -> foreach
        // Avoid interface enumerator allocation that would create.
        static double ConsumePreProcessedResourceUnits(ResourceContainerGroupSequence rcgs, ResourceDefinitionID resourceID, double totalUnitsToConsume)
        {
            double remaining = totalUnitsToConsume;
            //int i = 0;
            foreach (ResourceContainerGroup group in rcgs._groupsInSequence)
            //for (int count = rcgs.GroupsInSequence.Count; i < count; i++)
            {
                if (!(remaining > 0.0))
                {
                    break;
                }

                //ResourceContainerGroup resourceContainerGroup = group;
                //remaining -= group.ConsumePreProcessedResourceUnits(resourceID, remaining);
                remaining -= ConsumePreProcessedResourceUnits(group, resourceID, remaining);
            }

            return totalUnitsToConsume - remaining;
        }

        // changed:
        // for -> foreach
        // Avoid interface enumerator allocation that would create.
        static double RemoveResourceUnits(ResourceContainerGroupSequence rcgs, ResourceDefinitionID resourceID, double totalUnitsToRemove)
        {
            double remaining = totalUnitsToRemove;
            //int i = 0;
            //for (int count = rcgs.GroupsInSequence.Count; i < count; i++)
            foreach (ResourceContainerGroup group in rcgs._groupsInSequence)
            {
                if (!(remaining > 0.0))
                {
                    break;
                }

                //ResourceContainerGroup group = rcgs.GroupsInSequence[i];
                //remaining -= group.RemoveResourceUnits(resourceID, remaining);
                remaining -= RemoveResourceUnits(group, resourceID, remaining);
            }

            return totalUnitsToRemove - remaining;
        }
        #endregion

        #region ResourceContainerGroup "methods"
        // changed:
        // for -> foreach
        // Avoid interface enumerator allocation that would create.
        static double RemoveResourceUnits(ResourceContainerGroup rcg, ResourceDefinitionID resourceID, double totalUnitsToRemove)
        {
            if (totalUnitsToRemove == 0.0)
            {
                return 0.0;
            }

            //double resourceStoredUnits = rcg.GetResourceStoredUnits(resourceID);
            double resourceStoredUnits = GetResourceStoredUnits(rcg, resourceID);
            if (resourceStoredUnits <= totalUnitsToRemove)
            {
                rcg.DumpResource(resourceID);
                return resourceStoredUnits;
            }

            double num = totalUnitsToRemove / resourceStoredUnits;
            foreach (ResourceContainer container in rcg._containers)
            {
                double resourceStoredUnits2 = container.GetResourceStoredUnits(resourceID);
                double totalUnitsToRemove2 = num * resourceStoredUnits2;
                container.RemoveResourceUnits(resourceID, totalUnitsToRemove2);
            }

            return totalUnitsToRemove;
        }


        // changed:
        // for -> foreach
        // Avoid interface enumerator allocation that would create.
        static double GetResourceStoredUnits(ResourceContainerGroup rcg, ResourceDefinitionID resourceID)
        {
            double stored = 0.0;
            foreach (ResourceContainer container in rcg._containers)
            {
                stored += container.GetResourceStoredUnits(resourceID);
            }

            return stored;
        }

        static double AddResourceUnits(ResourceContainerGroup rcg, ResourceDefinitionID resourceId, double totalUnitsToAdd)
        {
            if (totalUnitsToAdd == 0.0)
            {
                return 0.0;
            }

            //double resourceEmptyUnits = rcg.GetResourceEmptyUnits(resourceID);
            double resourceEmptyUnits = 0;
            foreach (ResourceContainer container in rcg._containers)
            {
                resourceEmptyUnits += container.GetResourceEmptyUnits(resourceId, false);
            }

            if (resourceEmptyUnits <= totalUnitsToAdd)
            {
                //rcg.FillResourceToCapacity(resourceID);
                FillResourceToCapacity(rcg, resourceId);
                return resourceEmptyUnits;
            }

            double num = totalUnitsToAdd / resourceEmptyUnits;
            foreach (ResourceContainer container in rcg._containers)
            {
                double resourceEmptyUnits2 = container.GetResourceEmptyUnits(resourceId);
                double totalUnitsToAdd2 = num * resourceEmptyUnits2;
                container.AddResourceUnits(resourceId, totalUnitsToAdd2);
            }

            return totalUnitsToAdd;
        }

        static double FillResourceToCapacity(ResourceContainerGroup rcg, ResourceDefinitionID resourceID)
        {
            double added = 0.0;
            foreach (ResourceContainer container in rcg._containers)
            {
                added += container.FillResourceToCapacity(resourceID);
            }

            return added;
        }

        static double FillPreProcessedResourceToCapacity(ResourceContainerGroup rcg, ResourceDefinitionID resourceID)
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
        static double StorePreProcessedResourceUnits(ResourceContainerGroup rcg, ResourceDefinitionID resourceID, double totalUnitsToStore)
        {
            if (totalUnitsToStore == 0.0)
            {
                return 0.0;
            }

            //double num = rcg.GetResourceCapacityUnits(resourceID) - rcg.GetResourceStoredUnits(resourceID) + rcg.GetResourcePreProcessedUnits(resourceID);
            double num = 0;
            foreach (var container in rcg._containers)
            {
                num += container.GetResourceCapacityUnits(resourceID)
                    - container.GetResourceStoredUnits(resourceID)
                    + container.GetResourcePreProcessedUnits(resourceID);
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

            //IGAssert.IsTrue(totalUnitsToStore <= num, "Cannot store more preprocessed units than the capacity");
            return totalUnitsToStore;
        }

        // changed:
        // for -> foreach
        // Avoid interface enumerator allocation that would create.
        // fetch container preprocess/stored units for the same container in a single pass over the group.
        static double ConsumePreProcessedResourceUnits(ResourceContainerGroup rcg, ResourceDefinitionID resourceID, double totalUnitsToConsume)
        {
            if (totalUnitsToConsume == 0.0)
            {
                return 0.0;
            }

            //double resourcePreProcessedUnits = rcg.GetResourcePreProcessedUnits(resourceID);
            //resourcePreProcessedUnits += rcg.GetResourceStoredUnits(resourceID);
            // what to call this variable?
            double resourcePreProcessedUnits = 0;
            foreach (ResourceContainer container in rcg._containers)
            {
                resourcePreProcessedUnits += container.GetResourcePreProcessedUnits(resourceID);
                resourcePreProcessedUnits += container.GetResourceStoredUnits(resourceID);
            }

            if (resourcePreProcessedUnits <= totalUnitsToConsume)
            {
                return rcg.DumpPreProcessedResource(resourceID);
            }

            double num = totalUnitsToConsume / resourcePreProcessedUnits;
            foreach (ResourceContainer container in rcg._containers)
            {
                double resourcePreProcessedUnits2 = container.GetResourcePreProcessedUnits(resourceID);
                resourcePreProcessedUnits2 += container.GetResourceStoredUnits(resourceID);
                double totalUnitsToConsume2 = num * resourcePreProcessedUnits2;
                container.ConsumePreProcessedResourceUnits(resourceID, totalUnitsToConsume2);
            }

            return totalUnitsToConsume;
        }
        #endregion
    }
}