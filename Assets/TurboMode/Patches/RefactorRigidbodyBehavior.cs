using KSP.Sim;
using KSP.Sim.impl;
using System;
using System.Reflection;
using Unity.Profiling;
using UnityEngine;

namespace TurboMode.Patches
{
    public class RefactorRigidbodyBehavior
    {
        private static readonly ProfilerMarker SelectivePhysicsAutoSync_RigidbodyBehaviorOnUpdateEvents = new("SelectivePhysicsAutoSync.RigidbodyBehaviorOnUpdateEvents");
        private static readonly ProfilerMarker RefactorRigidbodyBehavior_UpdateTesorScale = new("RefactorRigidbodyBehavior.UpdateTesorScale");

        // private RigidbodyBehavior.MassScaleType
        enum MassScaleType
        {
            None,
            Explicit,
            InverseMass,
            InverseMassDifferential
        }

        // private fields
        private static readonly ReflectionUtil.FieldHelper<RigidbodyBehavior, PartOwnerBehavior> _ownerBehaviorField
            = new(typeof(RigidbodyBehavior).GetField("_ownerBehavior", BindingFlags.NonPublic | BindingFlags.Instance));
        private static readonly ReflectionUtil.FieldHelper<RigidbodyBehavior, bool> _isHandCorrectionCheckPendingField
            = new(typeof(RigidbodyBehavior).GetField("_isHandCorrectionCheckPending", BindingFlags.NonPublic | BindingFlags.Instance));

        private static readonly ReflectionUtil.FieldHelper<RigidbodyBehavior, bool> _isUnscaledInertiaTensorInitialized
            = new(typeof(RigidbodyBehavior).GetField("_isUnscaledInertiaTensorInitialized", BindingFlags.NonPublic | BindingFlags.Instance));
        private static readonly ReflectionUtil.FieldHelper<RigidbodyBehavior, Vector3> _unscaledInertiaTensor
            = new(typeof(RigidbodyBehavior).GetField("_unscaledInertiaTensor", BindingFlags.NonPublic | BindingFlags.Instance));
        private static readonly ReflectionUtil.FieldHelper<RigidbodyBehavior, int> _dynamicMassScaleType
            = new(typeof(RigidbodyBehavior).GetField("_dynamicMassScaleType", BindingFlags.NonPublic | BindingFlags.Instance));
        private static readonly ReflectionUtil.FieldHelper<RigidbodyBehavior, int> _massScaleType
            = new(typeof(RigidbodyBehavior).GetField("_massScaleType", BindingFlags.NonPublic | BindingFlags.Instance));
        private static readonly ReflectionUtil.FieldHelper<RigidbodyBehavior, float> _massScaleFactor
            = new(typeof(RigidbodyBehavior).GetField("_massScaleFactor", BindingFlags.NonPublic | BindingFlags.Instance));
        private static readonly ReflectionUtil.FieldHelper<RigidbodyBehavior, float> _globalTensorScalingOverride
            = new(typeof(RigidbodyBehavior).GetField("_globalTensorScalingOverride", BindingFlags.NonPublic | BindingFlags.Instance));

        // events
        private static readonly ReflectionUtil.EventHelper<RigidbodyBehavior, Action<Position>> positionUpdatedHelper
            = new(nameof(RigidbodyBehavior.PositionUpdated));
        private static readonly ReflectionUtil.EventHelper<RigidbodyBehavior, Action<Rotation>> rotationUpdatedHelper
            = new(nameof(RigidbodyBehavior.RotationUpdated));
        private static readonly ReflectionUtil.EventHelper<RigidbodyBehavior, Action<Velocity>> velocityUpdatedHelper
            = new(nameof(RigidbodyBehavior.VelocityUpdated));
        private static readonly ReflectionUtil.EventHelper<RigidbodyBehavior, Action<AngularVelocity>> angularVelocityUpdatedHelper
            = new(nameof(RigidbodyBehavior.AngularVelocityUpdated));

        // Performance goals:
        // - Make it robust to disabling autosync.  Avoid write-then-read that would affect Rigidbody accessor results.
        // - Avoid round trips between managed layer and c++ layer.
#pragma warning disable IDE0060 // Remove unused parameter
        public static void RigidbodyBehaviorOnUpdate(Action<object, float> orig, RigidbodyBehavior rbb, float deltaTime)
#pragma warning restore IDE0060 // Remove unused parameter
        {
            if (!rbb.enabled || !rbb.IsPhysXPositioned)
            {
                return;
            }

            // Note that syncs to the physics engine may be pending when this is set.
            // We're not guaranteed to have coherant Rigidbody.postion == Transform.position
            // even if the transform was changed while this was `true`.
            Physics.autoSyncTransforms = false;

            var _viewObject = rbb.ViewObject;
            var _physicsMode = rbb.PhysicsMode;
            var _ownerBehavior = _ownerBehaviorField.Get(rbb);
            var _isHandCorrectionCheckPending = _isHandCorrectionCheckPendingField.Get(rbb);
            var activeRigidBody = rbb.activeRigidBody;
            IPhysicsSpaceProvider physicsSpace = _viewObject.Universe.PhysicsSpace;

            Vector3 rbPosition;
            Quaternion rbRotation;
            Vector3 rbVelocity;
            Vector3 rbAngularVelocity;

            bool setPos = false;
            bool isVessel = _viewObject.PartOwner;
            bool isPart = _viewObject.Part;
            Transform controlledTransform = null;

            // get current data
            if (rbb.IsPhysXActive)
            {
                // Read the transform here because we won't see changes
                // that would affect activeRigidBody.position that are still queued.
                rbPosition = activeRigidBody.transform.position;
                rbRotation = activeRigidBody.rotation;
                rbVelocity = activeRigidBody.velocity;
                rbAngularVelocity = activeRigidBody.angularVelocity;
            }
            else
            {
                controlledTransform = rbb.transform;
                if (activeRigidBody)
                {
                    rbVelocity = activeRigidBody.velocity;
                    rbAngularVelocity = activeRigidBody.angularVelocity;
                }
                else
                {
                    rbVelocity = default;
                    rbAngularVelocity = default;
                }
                controlledTransform.GetPositionAndRotation(out rbPosition, out rbRotation);
            }

            // determine if we're manually moving or not, and calculate new positions if we are.
            if (_physicsMode == PartPhysicsModes.None)
            {
                _isHandCorrectionCheckPending = false;
                _isHandCorrectionCheckPendingField.Set(rbb, _isHandCorrectionCheckPending);
            }
            else if (_isHandCorrectionCheckPending)
            {
                if (isPart)
                {
                    _ownerBehavior = _viewObject.Part.partOwner;
                    _ownerBehaviorField.Set(rbb, _ownerBehavior);
                }
                else if (isVessel)
                {
                    _ownerBehavior = _viewObject.PartOwner;
                    _ownerBehaviorField.Set(rbb, _ownerBehavior);
                }

                if (_ownerBehavior != null && _ownerBehavior.IsHandOfKrakenCorrectingOrbit)
                {
                    bool physxStarting = _ownerBehavior.IsOwnerPhysXStarted && !_ownerBehavior.IsChildPhysXStarted;
                    Vector3d targetPos = physicsSpace.PositionToPhysics(_ownerBehavior.HandOfKrakenExpectedPos)
                        - (physicsSpace.PositionToPhysics(_ownerBehavior.Model.CenterOfMass)
                        - physicsSpace.PositionToPhysics(rbb.Model.Position));
                    Vector3d targetVelocity = physicsSpace.VelocityToPhysics(_ownerBehavior.HandOfKrakenExpectedVel, _ownerBehavior.HandOfKrakenExpectedPos);
                    Vector3 currentVelocityRelative = rbVelocity - _ownerBehavior.HandOfKrakenStartOfUpdateVel;
                    targetVelocity += currentVelocityRelative;
                    if (physxStarting)
                    {
                        // init if vessel?
                        if (isVessel)
                        {
                            controlledTransform = activeRigidBody.transform;
                            rbPosition = targetPos;
                            rbVelocity = targetVelocity;
                            setPos = true;
                        }
                        // ignore parts and everything else?
                    }
                    else if (isVessel)
                    {
                        // set vessel position to root part position?
                        _ownerBehavior = _viewObject.PartOwner;
                        _ownerBehavior.Model.TryGetPart(_ownerBehavior.Model.RootPart.GlobalId, out var part);
                        PartBehavior partBehavior = _ownerBehavior.GetPartViewComponent(part);
                        if (partBehavior != null)
                        {
                            controlledTransform = partBehavior.transform;
                            rbPosition = targetPos;
                            rbVelocity = targetVelocity;
                            setPos = true;
                        }
                    }
                    // if not the vessel?
                    else if (activeRigidBody != _ownerBehavior.ViewObject.Rigidbody.activeRigidBody)
                    {
                        controlledTransform = rbb.transform;
                        rbPosition = targetPos;
                        rbVelocity = targetVelocity;
                        setPos = true;
                    }
                }
                _isHandCorrectionCheckPending = false;
                _isHandCorrectionCheckPendingField.Set(rbb, _isHandCorrectionCheckPending);
            }

            // actually move
            if (setPos)
            {
                // Unity may not even queue these changes, so they may not be visible downstream
                // even after turn autoSync back on.  Collider positions will still be dirty,
                // but we definitely need accurate Rigidbody positions downstream.
                // So write to both seems to be enough to avoid needing a full sync, at least until
                // the universe OnUpdate() loop which does check colliders.
                controlledTransform.position = rbPosition;
                activeRigidBody.position = rbPosition;
                activeRigidBody.velocity = rbVelocity;
            }

            // report current data
            SelectivePhysicsAutoSync_RigidbodyBehaviorOnUpdateEvents.Begin(rbb);
            positionUpdatedHelper.Get(rbb)?.Invoke(physicsSpace.PhysicsToPosition(rbPosition));
            SelectivePhysicsAutoSync_RigidbodyBehaviorOnUpdateEvents.End();

            SelectivePhysicsAutoSync_RigidbodyBehaviorOnUpdateEvents.Begin(rbb);
            rotationUpdatedHelper.Get(rbb)?.Invoke(physicsSpace.PhysicsToRotation(rbRotation));
            SelectivePhysicsAutoSync_RigidbodyBehaviorOnUpdateEvents.End();

            if (rbb.IsPhysXActive || activeRigidBody)
            {
                SelectivePhysicsAutoSync_RigidbodyBehaviorOnUpdateEvents.Begin(rbb);
                velocityUpdatedHelper.Get(rbb)?.Invoke(physicsSpace.PhysicsToVelocity(rbVelocity));
                SelectivePhysicsAutoSync_RigidbodyBehaviorOnUpdateEvents.End();

                SelectivePhysicsAutoSync_RigidbodyBehaviorOnUpdateEvents.Begin(rbb);
                angularVelocityUpdatedHelper.Get(rbb)?.Invoke(physicsSpace.PhysicsToAngularVelocity(rbAngularVelocity));
                SelectivePhysicsAutoSync_RigidbodyBehaviorOnUpdateEvents.End();
            }

            Physics.autoSyncTransforms = true;

            // Calculate vessel CoM
            // No idea why a PartOwner update is in here.  This only applies to the vessel,
            // but the parts haven't been moved yet.
            if (isVessel)
            {
                SelectivePhysicsAutoSync_RigidbodyBehaviorOnUpdateEvents.Begin(rbb);
                OrbiterComponent orbiter = rbb.SimObjectComponent.SimulationObject.Orbiter;
                if (_viewObject.PartOwner.IsHandOfKrakenCorrectingOrbit)
                {
                    orbiter.PatchedOrbit.UpdateFromUT(rbb.Game.UniverseModel.UniverseTime);
                    _viewObject.PartOwner.Model.CenterOfMass = orbiter.Position;
                    SelectivePhysicsAutoSync_RigidbodyBehaviorOnUpdateEvents.End();
                    return;
                }
                _viewObject.PartOwner.GetMassAverages(out var averageCenterOfMass, out var averageVelocity);
                Position newPosition = physicsSpace.PhysicsToPosition(averageCenterOfMass);
                Velocity newVelocity = physicsSpace.PhysicsToVelocity(averageVelocity);
                orbiter.UpdateFromStateVectors(newPosition, newVelocity);
                _viewObject.PartOwner.Model.CenterOfMass = newPosition;
                SelectivePhysicsAutoSync_RigidbodyBehaviorOnUpdateEvents.End();
            }
        }

        // initial putting this here to get around reflection invoke,
        // but can possibly implment some Burst helpers to speed things up.
        private static readonly ReflectionUtil.FieldHelper<RigidbodyBehavior, Vector3d> localPositionField
            = new(typeof(RigidbodyBehavior).GetField("<localPosition>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance));
        private static readonly ReflectionUtil.FieldHelper<RigidbodyBehavior, QuaternionD> localRotationField
            = new(typeof(RigidbodyBehavior).GetField("<localRotation>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance));
        private static readonly ReflectionUtil.FieldHelper<RigidbodyBehavior, Vector> relativeVelocityField
            = new(typeof(RigidbodyBehavior).GetField("<relativeVelocity>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance));
        public static void UpdateToSimObject(RigidbodyBehavior rbb)
        {
            var activeRigidBody = rbb.activeRigidBody;
            var physicsSpace = rbb.ViewObject.Universe.PhysicsSpace;
            var coordinateSystem = rbb.Model.transform.parent;
            Transform thisUnityTransform = rbb.transform;

            bool hasActiveRigidbody = activeRigidBody != null;
            Position position = physicsSpace.PhysicsToPosition(thisUnityTransform.position);
            localPositionField.Set(rbb, coordinateSystem.ToLocalPosition(position));
            Rotation rotation = physicsSpace.PhysicsToRotation(thisUnityTransform.rotation);
            localRotationField.Set(rbb, coordinateSystem.ToLocalRotation(rotation));
            if (hasActiveRigidbody)
            {
                if (rbb.ViewObject.PartOwner == null)
                {
                    Velocity otherVelocity = physicsSpace.PhysicsToVelocity(activeRigidBody.velocity);
                    relativeVelocityField.Set(rbb, rbb.relativeToMotion.ToRelativeVelocity(otherVelocity, rbb.Position));
                }
                else
                {
                    PartOwnerComponent partOwner = rbb.SimObjectComponent.SimulationObject.PartOwner;
                    relativeVelocityField.Set(rbb, rbb.relativeToMotion.ToRelativeVelocity(partOwner.GetVelocityMassAverage(), rbb.Position));
                }
                if (rbb.ViewObject.PartOwner == null)
                {
                    AngularVelocity otherAngularVelocity = physicsSpace.PhysicsToAngularVelocity(activeRigidBody.angularVelocity);
                    rbb.relativeAngularVelocity = rbb.relativeToMotion.ToRelativeAngularVelocity(otherAngularVelocity);
                }
                else
                {
                    rbb.relativeAngularVelocity = rbb.relativeToMotion.ToRelativeAngularVelocity(rbb.Model.SimulationObject.PartOwner.AngularVelocityMassAvg);
                }
            }
            positionUpdatedHelper.Get(rbb)?.Invoke(rbb.Position);
            rotationUpdatedHelper.Get(rbb)?.Invoke(rbb.Rotation);
            if (hasActiveRigidbody)
            {
                velocityUpdatedHelper.Get(rbb)?.Invoke(rbb.Velocity);
                angularVelocityUpdatedHelper.Get(rbb)?.Invoke(rbb.AngularVelocity);
                rbb.Model.inertiaTensor = physicsSpace.PhysicsToVector(activeRigidBody.inertiaTensor);
            }
        }

        // Replacement for the tensor scaling code in RigidbodyBehavior.OnPhysicsUpdate() but with the following simplifications:
        // - Assume PhysicsSettings.ENABLE_DYNAMIC_TENSOR_SOLUTION == false;
        // - Assume PhysicsSettings.ENABLE_INERTIA_TENSOR_SCALING == true
        // - Remove some null checks because we're guarding the nulls externally.
        public static void UpdateTesorScale(RigidbodyComponent rbc, RigidbodyBehavior rbb)
        {
            RefactorRigidbodyBehavior_UpdateTesorScale.Begin(rbb);
            // This is never set, but need to get it because it's serialized field that can affect chosen scale type,
            // and I haven't proved they're all the same..
            MassScaleType dynamicMassScaleType = (MassScaleType)_dynamicMassScaleType.Get(rbb);
            MassScaleType massScaleType = (MassScaleType)_massScaleType.Get(rbb);
            float massScaleFactor = _massScaleFactor.Get(rbb);
            bool isUnscaledInertiaTensorInitialized;

            var activeRigidBody = rbb.activeRigidBody;

            MassScaleType usedMassScaleType = massScaleType;
            if (usedMassScaleType != dynamicMassScaleType)
            {
                PartComponent partComponent = rbc.SimulationObject.Part;
                PartOwnerComponent partOwner = partComponent.PartOwner;
                // part may be orphaned
                if (partOwner != null && partOwner.PartCount == 1 && partOwner.RootPart == partComponent)
                {
                    if (rbb.mass <= PhysicsSettings.GLOBAL_LOWMASS_TENSOR_LIMIT)
                    {
                        usedMassScaleType = MassScaleType.Explicit;
                        massScaleFactor = PhysicsSettings.GLOBAL_LOWMASS_TENSOR_SCALAR;
                    }
                    else
                    {
                        usedMassScaleType = MassScaleType.None;
                    }
                }
            }
            switch (usedMassScaleType)
            {
                case MassScaleType.InverseMass:
                    float globalTensorScalingOverride = _globalTensorScalingOverride.Get(rbb);
                    float tensorScale = PhysicsSettings.GLOBAL_TENSOR_SCALAR;
                    if (!Mathf.Approximately(globalTensorScalingOverride, tensorScale))
                    {
                        tensorScale = globalTensorScalingOverride;
                    }
                    massScaleFactor = tensorScale / activeRigidBody.mass;
                    ScaleInertiaTensor(rbb, activeRigidBody, massScaleFactor);
                    break;
                case MassScaleType.InverseMassDifferential:
                    isUnscaledInertiaTensorInitialized = _isUnscaledInertiaTensorInitialized.Get(rbb);
                    Joint joint = rbb.GetComponent<Joint>();
                    if (joint != null)
                    {
                        Rigidbody jointConnectedBody = joint.connectedBody;
                        if (jointConnectedBody != null)
                        {
                            massScaleFactor = jointConnectedBody.mass / activeRigidBody.mass;
                            ScaleInertiaTensor(rbb, activeRigidBody, massScaleFactor);
                        }
                    }
                    else if (isUnscaledInertiaTensorInitialized)
                    {
                        ResetInertialTensor(rbb, activeRigidBody);
                    }
                    break;
                case MassScaleType.Explicit:
                    ScaleInertiaTensor(rbb, activeRigidBody, massScaleFactor);
                    break;
                case MassScaleType.None:
                    if (_isUnscaledInertiaTensorInitialized.Get(rbb))
                    {
                        ResetInertialTensor(rbb, activeRigidBody);
                    }
                    break;
            }

            RefactorRigidbodyBehavior_UpdateTesorScale.End();
        }

        private static void ScaleInertiaTensor(RigidbodyBehavior rbb, Rigidbody rb, float scalar)
        {
            if (!_isUnscaledInertiaTensorInitialized.Get(rbb))
            {
                _unscaledInertiaTensor.Set(rbb, rb.inertiaTensor);
                _isUnscaledInertiaTensorInitialized.Set(rbb, true);
            }
            float currentMass = rb.mass;
            rb.inertiaTensor = _unscaledInertiaTensor.Get(rbb) * scalar;
            rb.mass = currentMass;
            rb.WakeUp();
            _massScaleFactor.Set(rbb, scalar);
        }

        private static void ResetInertialTensor(RigidbodyBehavior rbb, Rigidbody activeRigidbody)
        {
            activeRigidbody.ResetInertiaTensor();
            _isUnscaledInertiaTensorInitialized.Set(rbb, false);
        }
    }
}
