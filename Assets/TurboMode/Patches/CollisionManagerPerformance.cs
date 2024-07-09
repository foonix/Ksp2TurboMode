using HarmonyLib;
using KSP.Sim.impl;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Unity.Profiling;
using UnityEngine;

namespace TurboMode
{
    public static class CollisionManagerPerformance
    {
        static readonly Dictionary<CollisionManager, VesselData> vesselData = new();
        static readonly List<Collider> _tempColliderStorage = new();
        static readonly List<ColliderData> _tempColliderDataStorage = new();

        static readonly ProfilerMarker CollisionManagerPerformance_AddPartAdditive = new("CollisionManagerPerformance.AddPartAdditive");

        #region Harmony patching
        static readonly MethodInfo cmUpdateIgnored = AccessTools.Method(typeof(CollisionManager), "OnCollisionIgnoreUpdate");
        static readonly MethodInfo tmAddPartAdditive = AccessTools.Method(typeof(CollisionManagerPerformance), "AddPartAdditive");

        [HarmonyPatch(typeof(PartBehavior), "Start")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> PartAddCollidersAdditive_Patch(IEnumerable<CodeInstruction> instructions)
        {
            var found = false;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(cmUpdateIgnored))
                {
                    // CollisionManager is already on the stack
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, tmAddPartAdditive);
                    found = true;
                }
                else
                {
                    yield return instruction;
                }
            }
            if (found is false)
                Debug.Log("TM: Cannot find <Stdfld someField> in OriginalType.OriginalMethod");
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(CollisionManager), nameof(CollisionManager.OnCollisionIgnoreUpdate))]
        public static void LogMissedCall()
        {
            Debug.LogError("Process called to OnCollisionIgnoreUpdate");
        }
        #endregion

        private struct VesselData
        {
            public VesselBehavior vessel;
            public bool collidersAreActive;
            public bool colliderDataIsDirty;
            public readonly List<ColliderData> colliders;

            public VesselData(VesselBehavior vessel)
            {
                this.vessel = vessel;
                colliders = new();
                collidersAreActive = false;
                colliderDataIsDirty = false;
            }
        }

        private struct ColliderData
        {
            public bool hasNoSameVesselCollisionTag;
            public Collider collider;

            public ColliderData(Collider collider)
            {
                hasNoSameVesselCollisionTag = collider.CompareTag("NoSameVesselCollision");
                this.collider = collider;
            }

            public static implicit operator Collider(ColliderData d) => d.collider;
        }

        public static void AddPartAdditive(CollisionManager cm, PartBehavior part)
        {
            CollisionManagerPerformance_AddPartAdditive.Begin(cm);
            var vesselData = GetOrCreateVesselData(cm);
            bool isKerbalEVA = cm.Vessel.SimObjectComponent.IsKerbalEVA;

            Transform transform = part.FindModelTransform("model");

            CollisionManagerPerformance_AddPartAdditive.Begin(cm);
            if (transform != null)
            {
                transform.GetComponentsInChildren(isKerbalEVA, _tempColliderStorage);
            }
            else
            {
                //GetPartColliders(part.transform, isKerbalEVA, ref _tempColliderStorage);
                cm.CallPrivateVoidMethod("GetPartColliders", part.transform, isKerbalEVA, _tempColliderStorage);
            }
            CollisionManagerPerformance_AddPartAdditive.End();

            foreach (var partCollider in _tempColliderStorage)
            {
                var partColliderData = new ColliderData(partCollider);
                _tempColliderDataStorage.Add(partColliderData);

                foreach (var collider in vesselData.colliders)
                {
                    if (partColliderData.hasNoSameVesselCollisionTag && collider.hasNoSameVesselCollisionTag)
                    {
                        Physics.IgnoreCollision(partCollider, collider, ignore: true);
                    }
                }
            }

            // AddRange creates garbage.
            foreach (var tmp in _tempColliderDataStorage)
            {
                vesselData.colliders.Add(tmp);
            }
            _tempColliderStorage.Clear();
            _tempColliderDataStorage.Clear();
            CollisionManagerPerformance_AddPartAdditive.End();
        }

        private static VesselData GetOrCreateVesselData(CollisionManager cm)
        {
            if (vesselData.TryGetValue(cm, out VesselData data))
            {
                return data;
            }

            data = new VesselData(cm.Vessel);
            vesselData.Add(cm, data);

            return data;
        }
    }
}
