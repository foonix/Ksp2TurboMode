using KSP.Sim.impl;
using KSP.Sim;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using System;
using Unity.Profiling;

namespace TurboMode.Patches
{
    public class RefactorRigidbodyBehavior
    {
        private static readonly ProfilerMarker SelectivePhysicsAutoSync_RigidbodyBehaviorOnUpdateEvents = new("SelectivePhysicsAutoSync.RigidbodyBehaviorOnUpdateEvents");

        // private fields
        private static readonly FieldInfo _ownerBehaviorField
            = typeof(RigidbodyBehavior).GetField("_ownerBehavior", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo _isHandCorrectionCheckPendingField
            = typeof(RigidbodyBehavior).GetField("_isHandCorrectionCheckPending", BindingFlags.NonPublic | BindingFlags.Instance);

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

            var _viewObject = rbb.ViewObject;
            var _physicsMode = rbb.PhysicsMode;
            var _ownerBehavior = (PartOwnerBehavior)_ownerBehaviorField.GetValue(rbb);
            var _isHandCorrectionCheckPending = (bool)_isHandCorrectionCheckPendingField.GetValue(rbb);
            var activeRigidBody = rbb.activeRigidBody;

            Velocity velocity = default;
            AngularVelocity angularVelocity = default;
            IPhysicsSpaceProvider physicsSpace = _viewObject.Universe.PhysicsSpace;
            if (_physicsMode == PartPhysicsModes.None)
            {
                _isHandCorrectionCheckPending = false;
                _isHandCorrectionCheckPendingField.SetValue(rbb, _isHandCorrectionCheckPending);
            }
            else if (_isHandCorrectionCheckPending)
            {
                if (_viewObject.Part != null)
                {
                    _ownerBehavior = _viewObject.Part.partOwner;
                    _ownerBehaviorField.SetValue(rbb, _ownerBehavior);
                }
                else if (_viewObject.PartOwner != null)
                {
                    _ownerBehavior = _viewObject.PartOwner;
                    _ownerBehaviorField.SetValue(rbb, _ownerBehavior);
                }
                if (_ownerBehavior != null && _ownerBehavior.IsHandOfKrakenCorrectingOrbit)
                {
                    bool physxStarting = _ownerBehavior.IsOwnerPhysXStarted && !_ownerBehavior.IsChildPhysXStarted;
                    Vector3d targetPos = physicsSpace.PositionToPhysics(_ownerBehavior.HandOfKrakenExpectedPos)
                        - (physicsSpace.PositionToPhysics(_ownerBehavior.Model.CenterOfMass)
                        - physicsSpace.PositionToPhysics(rbb.Model.Position));
                    Vector3d targetVelocity = physicsSpace.VelocityToPhysics(_ownerBehavior.HandOfKrakenExpectedVel, _ownerBehavior.HandOfKrakenExpectedPos);
                    Vector3 currentVelocityRelative = activeRigidBody.velocity - _ownerBehavior.HandOfKrakenStartOfUpdateVel;
                    targetVelocity += currentVelocityRelative;
                    if (physxStarting)
                    {
                        if (_viewObject.PartOwner != null)
                        {
                            activeRigidBody.transform.position = targetPos;
                            activeRigidBody.velocity = targetVelocity;
                        }
                    }
                    else if (_viewObject.PartOwner != null)
                    {
                        _ownerBehavior = _viewObject.PartOwner;
                        _ownerBehavior.Model.TryGetPart(_ownerBehavior.Model.RootPart.GlobalId, out var part);
                        PartBehavior partViewComponent = _ownerBehavior.GetPartViewComponent(part);
                        if (partViewComponent != null)
                        {
                            partViewComponent.transform.position = targetPos;
                            activeRigidBody.velocity = targetVelocity;
                        }
                    }
                    else if (activeRigidBody != _ownerBehavior.ViewObject.Rigidbody.activeRigidBody)
                    {
                        rbb.transform.position = targetPos;
                        activeRigidBody.velocity = targetVelocity;
                    }
                }
                _isHandCorrectionCheckPending = false;
                _isHandCorrectionCheckPendingField.SetValue(rbb, _isHandCorrectionCheckPending);
            }
            Position position;
            Rotation rotation;
            if (rbb.IsPhysXActive)
            {
                position = physicsSpace.PhysicsToPosition(activeRigidBody.position);
                rotation = physicsSpace.PhysicsToRotation(activeRigidBody.rotation);
                velocity = physicsSpace.PhysicsToVelocity(activeRigidBody.velocity);
                angularVelocity = physicsSpace.PhysicsToAngularVelocity(activeRigidBody.angularVelocity);
            }
            else
            {
                position = physicsSpace.PhysicsToPosition(rbb.transform.position);
                rotation = physicsSpace.PhysicsToRotation(rbb.transform.rotation);
                if (activeRigidBody != null)
                {
                    velocity = physicsSpace.PhysicsToVelocity(activeRigidBody.velocity);
                    angularVelocity = physicsSpace.PhysicsToAngularVelocity(activeRigidBody.angularVelocity);
                }
            }

            SelectivePhysicsAutoSync_RigidbodyBehaviorOnUpdateEvents.Begin(rbb);
            positionUpdatedHelper.Get(rbb).Invoke(position);
            SelectivePhysicsAutoSync_RigidbodyBehaviorOnUpdateEvents.End();

            SelectivePhysicsAutoSync_RigidbodyBehaviorOnUpdateEvents.Begin(rbb);
            rotationUpdatedHelper.Get(rbb).Invoke(rotation);
            SelectivePhysicsAutoSync_RigidbodyBehaviorOnUpdateEvents.End();

            if (activeRigidBody != null)
            {
                SelectivePhysicsAutoSync_RigidbodyBehaviorOnUpdateEvents.Begin(rbb);
                velocityUpdatedHelper.Get(rbb).Invoke(velocity);
                SelectivePhysicsAutoSync_RigidbodyBehaviorOnUpdateEvents.End();

                SelectivePhysicsAutoSync_RigidbodyBehaviorOnUpdateEvents.Begin(rbb);
                angularVelocityUpdatedHelper.Get(rbb).Invoke(angularVelocity);
                SelectivePhysicsAutoSync_RigidbodyBehaviorOnUpdateEvents.End();
            }
            if (_viewObject.PartOwner != null)
            {
                OrbiterComponent orbiter = rbb.SimObjectComponent.SimulationObject.Orbiter;
                if (_viewObject.PartOwner.IsHandOfKrakenCorrectingOrbit)
                {
                    orbiter.PatchedOrbit.UpdateFromUT(rbb.Game.UniverseModel.UniverseTime);
                    _viewObject.PartOwner.Model.CenterOfMass = orbiter.Position;
                    return;
                }
                _viewObject.PartOwner.GetMassAverages(out var averageCenterOfMass, out var averageVelocity);
                Position newPosition = physicsSpace.PhysicsToPosition(averageCenterOfMass);
                Velocity newVelocity = physicsSpace.PhysicsToVelocity(averageVelocity);
                orbiter.UpdateFromStateVectors(newPosition, newVelocity);
                _viewObject.PartOwner.Model.CenterOfMass = newPosition;
            }
        }
    }
}
