using KSP.Sim.impl;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using System;
using System.Collections.Generic;
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

        public static List<IDetour> MakeHooks() => new()
        {
            new ILHook(typeof(PartBehavior).GetMethod("Start",System.Reflection.BindingFlags.NonPublic |System.Reflection.BindingFlags.Instance), PartBehavior_Start_Patch),
            new Hook(typeof(CollisionManager).GetMethod("OnCollisionIgnoreUpdate"), (Action<CollisionManager>)LogMissedCall),
        };

        public static void LogMissedCall(CollisionManager _) => Debug.LogError("Process called to OnCollisionIgnoreUpdate");

        static void PartBehavior_Start_Patch(ILContext context)
        {
            ILCursor cursor = new(context);
            cursor.GotoNext(inst => inst.MatchCallvirt(typeof(CollisionManager), "OnCollisionIgnoreUpdate"));
            cursor.Remove();  // we are replacing this call
            // CollisionManager is already on the stack
            cursor
                .Emit(OpCodes.Ldarg_0) // this (PartBehavior)
                .Emit(OpCodes.Callvirt, typeof(CollisionManagerPerformance).GetMethod("AddPartAdditive"));
        }

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
