using KSP.Game;
using KSP.Sim;
using KSP.Sim.impl;
using KSP.Sim.ResourceSystem;
using RTG;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static KSP.Sim.impl.VesselBehavior;
using UnityEngine.UIElements;
using static KSP.Sim.ResourceSystem.ResourceFlowRequestManager;
using Unity.Profiling;
using MonoMod.RuntimeDetour;


namespace TurboMode.Patches
{
    public class FlowRequests
    {
        private static readonly ReflectionUtil.EventHelper<ResourceFlowRequestManager, Action> requestsUpdatedHelper
            = new(nameof(ResourceFlowRequestManager.RequestsUpdated));

        private static readonly ProfilerMarker UpdateFlowRequestsMarker = new("TurboMode.Models.FlowRequests.UpdateFlowRequests");

        public static List<IDetour> MakeHooks() => new()
        {
            new Hook(
                typeof(ResourceFlowRequestManager).GetMethod("UpdateFlowRequests"),
                (Action<Action<ResourceFlowRequestManager, double, double>, ResourceFlowRequestManager, double, double>)UpdateFlowRequests
                ),
        };

        public static void UpdateFlowRequests(
            Action<ResourceFlowRequestManager, double, double> orig,
            ResourceFlowRequestManager rfrm,
            double tickUniversalTime, double tickDeltaTime)
        {
            using var marker = UpdateFlowRequestsMarker.Auto();

            if (GameManager.Instance != null && GameManager.Instance.Game != null && GameManager.Instance.Game.SessionManager != null)
            {
                rfrm._infiniteFuelEnabled = GameManager.Instance.Game.SessionManager.IsDifficultyOptionEnabled("InfiniteFuel");
                rfrm._infiniteECEnabled = GameManager.Instance.Game.SessionManager.IsDifficultyOptionEnabled("InfinitePower");
            }
            else
            {
                rfrm._infiniteFuelEnabled = false;
                rfrm._infiniteECEnabled = false;
            }
            rfrm._orderedRequests.Clear();
            foreach (ResourceFlowRequestHandle activeRequest in rfrm._activeRequests)
            {
                if (rfrm.GetRequestWrapperInternal(activeRequest, out var wrapper))
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
            rfrm.ProcessActiveRequests(rfrm._orderedRequests, tickUniversalTime, tickDeltaTime);
            if (rfrm._orderedRequests.Count > 0)
            {
                requestsUpdatedHelper.Get(rfrm)?.Invoke();
            }
        }
    }
}