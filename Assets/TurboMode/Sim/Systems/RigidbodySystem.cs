using KSP.Game;
using KSP.Sim;
using KSP.Sim.impl;
using MonoMod.RuntimeDetour;
using System;
using System.Reflection;
using TurboMode.Patches;
using TurboMode.Sim.Components;
using Unity.Entities;
using Unity.Profiling;
using UnityEngine;

namespace TurboMode.Sim.Systems
{
    /// <summary>
    /// Replaces RigidbodyBehavior.OnFixedUpdate
    /// 
    /// optimization:
    /// - Get the vessels' gravity once, instead of for each rigidbody (except disconencted rigidbodies)
    /// </summary>
    [DisableAutoCreation]
    public partial class RigidbodySystem : SystemBase
    {
#pragma warning disable IDE0052 // Remove unread private members
        readonly Hook rbbOnFixedUpdateShutoff = new(
                typeof(RigidbodyBehavior).GetMethod("OnFixedUpdate"),
                (Action<Action<System.Object, float>, IFixedUpdate, float>)SuppressionUtils.FixedUpdateShunt
            );
        readonly Hook partComponentMassUpdateShutoff = new(
                typeof(PartComponent).GetMethod("UpdateMass"),
                (Action<Action<object>, object>)SuppressionUtils.VoidShutoff
            );
        readonly Hook handOfCrakenOnUpdateShutoff = new(
            typeof(HandOfKraken).GetMethod("OnUpdate"),
            (Action<Action<System.Object, float>, IUpdate, float>)SuppressionUtils.UpdateShunt
        );
#pragma warning restore IDE0052 // Remove unread private members

        private static readonly ProfilerMarker s_RbbFixedUpdate = new("RigidbodySystem RigidbodyBehavior.FixedUpdate()");

        private static readonly ReflectionUtil.FieldHelper<PartComponent, double> partMassField
            = new(typeof(PartComponent).GetField("mass", BindingFlags.NonPublic | BindingFlags.Instance));
        private static readonly ReflectionUtil.FieldHelper<PartComponent, double> partGreenMassField
            = new(typeof(PartComponent).GetField("greenMass", BindingFlags.NonPublic | BindingFlags.Instance));
        private static readonly ReflectionUtil.FieldHelper<PartComponent, double> partResourceMass
            = new(typeof(PartComponent).GetField("resourceMass", BindingFlags.NonPublic | BindingFlags.Instance));
        private static readonly ReflectionUtil.FieldHelper<PartComponent, double> partPhysicsMass
            = new(typeof(PartComponent).GetField("physicsMass", BindingFlags.NonPublic | BindingFlags.Instance));

        static ComponentLookup<Vessel> vesselLookup;

        protected override void OnCreate()
        {
            base.OnCreate();
            vesselLookup = GetComponentLookup<Vessel>(true);
        }

        protected override void OnUpdate()
        {
            var game = GameManager.Instance.Game;
            if (!game.IsSimulationRunning())
            {
                return;
            }

            vesselLookup.Update(this);

            Entities
                .WithName("WritePartMass")
                .ForEach(
                (in Part part, in Components.RigidbodyComponent rbc, in SimObject simObj) =>
                {
                    var partComponent = simObj.inUniverse.Part;
                    partMassField.Set(partComponent, rbc.effectiveMass);
                    partPhysicsMass.Set(partComponent, rbc.effectiveMass);
                    partGreenMassField.Set(partComponent, part.greenMass);
                    partResourceMass.Set(partComponent, part.wetMass);
                })
                .WithoutBurst()
                .Run();

            Entities
                .WithName("ReadVesselData")
                .ForEach(
                (ref Vessel vessel, in SimObject simObj) =>
                {
                    // cache vessel gravity for active physic parts
                    var vesselObj = simObj.inUniverse;
                    if (vesselObj.objVesselBehavior)
                    {
                        vessel.gravityAtCurrentLocation = vesselObj.objVesselBehavior.PartOwner.GetGravityForceAtCurrentPosition();

                        Vessel.Flags flags = default;
                        if (vesselObj.objVesselBehavior.PartOwner.IsHandOfKrakenCorrectingOrbit)
                        {
                            flags |= Vessel.Flags.IsHandOfKrakenCorrectingOrbit;
                        }
                        vessel.flags = flags;
                    }
                    // TODO: drive whatever is running the vessel mass update
                })
                .WithoutBurst()
                .Run();

            Entities
                .WithName("UpdateRbForcesVessel")
                .ForEach(
                (ref Components.RigidbodyComponent rbc, in Vessel vessel, in SimObject simObj) =>
                {
                    //var vessel = vesselLookup[simObj.owner];
                    rbc.accelerations = vessel.gravityAtCurrentLocation;

                    var rbObj = simObj.inUniverse;
                    var sim = GameManager.Instance.Game.SpaceSimulation;
                    var rbView = sim.ModelViewMap.FromModel(rbObj);

                    if (!rbView || !rbView.Rigidbody /*|| (!rbView.Rigidbody.activeRigidBody && rbObj.Vessel == null)*/ )
                    {
                        return;
                    }

                    var rbb = rbView.Rigidbody;
                    s_RbbFixedUpdate.Begin(rbb);
                    UpdateRbForces(rbb, sim.UniverseModel, vessel);
                    //_isHandCorrectionCheckPendingField.Set(rbb, false);
                    //RefactorRigidbodyBehavior.VesselUpdateCom(sim.UniverseView.PhysicsSpace, rbView, rbb);
                    s_RbbFixedUpdate.End();
                })
                .WithoutBurst()
                .Run();

            Entities
                .WithName("UpdateRbForces")
                .WithAbsent<Vessel>()
                .ForEach(
                (ref Components.RigidbodyComponent rbc, in SimObject simObj) =>
                {
                    var vessel = vesselLookup[simObj.owner];
                    rbc.accelerations = vessel.gravityAtCurrentLocation;

                    var rbObj = simObj.inUniverse;
                    var sim = GameManager.Instance.Game.SpaceSimulation;
                    var rbView = sim.ModelViewMap.FromModel(rbObj);

                    if (!rbView || !rbView.Rigidbody /*|| (!rbView.Rigidbody.activeRigidBody && rbObj.Vessel == null)*/ )
                    {
                        return;
                    }

                    var rbb = rbView.Rigidbody;
                    s_RbbFixedUpdate.Begin(rbb);
                    UpdateRbForces(rbb, sim.UniverseModel, vessel);
                    _isHandCorrectionCheckPendingField.Set(rbb, false);
                    s_RbbFixedUpdate.End();
                })
                .WithoutBurst()
                .Run();
        }

        // rewritten version of RigidbodyBehavior.OnFixedUpdate()
        // Unfortunately it'll be hard to eliminate this until everything it touches in in ECS..
        // So just do obvious optimizations like moving inner loop stuff out for now.
        private static readonly ReflectionUtil.FieldHelper<RigidbodyBehavior, bool> _isHandCorrectionCheckPendingField
            = new(typeof(RigidbodyBehavior).GetField("_isHandCorrectionCheckPending", BindingFlags.NonPublic | BindingFlags.Instance));
        private static void UpdateRbForces(RigidbodyBehavior rbb, UniverseModel universeModel, in Vessel vessel)
        {
            var model = rbb.Model;
            var activeRigidBody = rbb.activeRigidBody;
            var localToWorldMatrix = rbb.transform.localToWorldMatrix;
            var part = rbb.ViewObject.Part;
            var vesselBehavior = rbb.ViewObject.Vessel;
            var rbbIsEnabled = rbb.enabled;
            var gravity = vessel.gravityAtCurrentLocation;

            // redo of hand correction code in RigidbodyBehaviorOnUpdate()
            //if (rbb.PhysicsMode != PartPhysicsModes.None && (vessel.flags | Vessel.Flags.IsHandOfKrakenCorrectingOrbit) > 0
            //    /*&& part*/ && activeRigidBody)
            //{
            //    IPhysicsSpaceProvider physicsSpace = rbb.ViewObject.Universe.PhysicsSpace;

            //    // for a first go at this, I want to try not doing the dance with the vessel updating the root part
            //    // and the root part not updating its self.
            //    Vector3d targetPos = physicsSpace.PositionToPhysics(owner.HandOfKrakenExpectedPos)
            //        - (physicsSpace.PositionToPhysics(owner.Model.CenterOfMass)
            //        - physicsSpace.PositionToPhysics(rbb.Model.Position));
            //    Vector3d targetVelocity = physicsSpace.VelocityToPhysics(owner.HandOfKrakenExpectedVel, owner.HandOfKrakenExpectedPos);
            //    Vector3 currentVelocityRelative = activeRigidBody.velocity - owner.HandOfKrakenStartOfUpdateVel;
            //    targetVelocity += currentVelocityRelative;

            //    rbb.transform.position = targetPos;
            //    activeRigidBody.velocity = targetVelocity;
            //}

            if (rbbIsEnabled)
                RefactorRigidbodyBehavior.UpdateToSimObject(rbb, activeRigidBody);

            if (!activeRigidBody)
            {
                return;
            }

            Physics.autoSyncTransforms = false;
            if (Math.Abs(rbb.mass - model.mass) > PhysicsSettings.PHYSX_MASS_TOLERANCE)
            {
                rbb.mass = model.mass;
            }

            if (rbbIsEnabled && rbb.IsPhysXActive && !activeRigidBody.useGravity
                && gravity.sqrMagnitude > PhysicsSettings.PHYSX_RB_SQR_MAG_GRAVITY_THRESHOLD)
            {
                activeRigidBody.AddForce(gravity, ForceMode.Acceleration);
            }

            foreach (var force in model.Forces)
            {
                if (force.RelativeForce.sqrMagnitude > PhysicsSettings.PHYSX_RB_SQR_MAG_THRESHOLD)
                {
                    Vector3 force2 = localToWorldMatrix.MultiplyVector(force.RelativeForce);
                    Vector3 position = localToWorldMatrix.MultiplyPoint(force.RelativePosition);
                    activeRigidBody.AddForceAtPosition(force2, position, (force.ForceMode == ForceType.Acceleration) ? ForceMode.Acceleration : ForceMode.Force);
                }
            }
            foreach (var force in model.SingleFrameForces)
            {
                if (force.RelativeForce.sqrMagnitude > PhysicsSettings.PHYSX_RB_SQR_MAG_THRESHOLD)
                {
                    Vector3 force4 = localToWorldMatrix.MultiplyVector(force.RelativeForce);
                    Vector3 position2 = localToWorldMatrix.MultiplyPoint(force.RelativePosition);
                    activeRigidBody.AddForceAtPosition(force4, position2, (force.ForceMode == ForceType.Acceleration) ? ForceMode.Acceleration : ForceMode.Force);
                }
            }
            model.ClearSingleFrameForces();
            foreach (var torque in model.Torques)
            {
                Vector3d vector3d2 = localToWorldMatrix.MultiplyVector(torque.RelativeTorque);
                if (vector3d2.sqrMagnitude > PhysicsSettings.PHYSX_RB_SQR_MAG_THRESHOLD)
                {
                    activeRigidBody.AddTorque(vector3d2, (torque.TorqueMode == ForceType.Acceleration) ? ForceMode.Acceleration : ForceMode.Force);
                }
            }
            foreach (var impulse in model.PendingImpulses)
            {
                Vector3 vector = localToWorldMatrix.MultiplyVector(impulse.RelativeImpulse);
                if ((double)vector.sqrMagnitude > PhysicsSettings.PHYSX_RB_SQR_MAG_THRESHOLD)
                {
                    activeRigidBody.AddForce(vector, (impulse.ForceMode != ForceType.Acceleration) ? ForceMode.Impulse : ForceMode.VelocityChange);
                }
                Vector3 vector2 = activeRigidBody.transform.localToWorldMatrix.MultiplyPoint(activeRigidBody.centerOfMass);
                Vector3 torque2 = Vector3.Cross(localToWorldMatrix.MultiplyPoint(impulse.RelativePosition) - vector2, vector);
                if ((double)torque2.sqrMagnitude > PhysicsSettings.PHYSX_RB_SQR_MAG_THRESHOLD)
                {
                    activeRigidBody.AddTorque(torque2, (impulse.ForceMode != ForceType.Acceleration) ? ForceMode.Impulse : ForceMode.VelocityChange);
                }
            }
            model.ClearPendingImpulses();

            if (rbb.IsPhysXActive)
            {
                double num3;
                if (part)
                {
                    CelestialBodyComponent partCelestialBody = part.Model.PartCelestialBody;
                    if (partCelestialBody.hasOcean && part.SubmergedPercent > 0.0)
                    {
                        double num4 = partCelestialBody.oceanDensity * 1000.0;
                        double num5 = num4 * 0.0005 * (double)activeRigidBody.velocity.sqrMagnitude;
                        num3 = ((!(part.SubmergedPercent >= 1.0)) ? (part.staticPressureAtm * (1.0 - part.SubmergedPercent) + part.SubmergedPercent * num4 * PhysicsSettings.BuoyancyWaterAngularDragScalar * part.Model.LiquidDragScalar) : (num4 * PhysicsSettings.BuoyancyWaterAngularDragScalar * part.Model.LiquidDragScalar));
                        num3 += part.Model.dynamicPressurekPa * 0.0098692326671601278 * (1.0 - part.SubmergedPercent);
                        num3 += num5 * 0.0098692326671601278 * PhysicsSettings.BuoyancyWaterAngularDragScalar * part.Model.LiquidAngularDragScalar * part.SubmergedPercent;
                    }
                    else
                    {
                        num3 = part.Model.dynamicPressurekPa * 0.0098692326671601278;
                    }
                }
                else if (vesselBehavior)
                {
                    num3 = vesselBehavior.Model.DynamicPressure_kPa * 0.0098692326671601278;
                }
                else
                {
                    var position = model.transform.Position;
                    CelestialBodyComponent mainBody = universeModel.GetMainBody(position);
                    double altitudeFromRadius = mainBody.GetAltitudeFromRadius(position);
                    double pressure = mainBody.GetPressure(altitudeFromRadius);
                    double temperature = mainBody.GetTemperature(altitudeFromRadius);
                    num3 = mainBody.GetDynamicPressurekPa(pressure, temperature, mainBody.transform.bodyFrame, model.transform) * 0.0098692326671601278;
                }
                if (num3 <= 0.0)
                {
                    num3 = 0.0;
                }
                float num6 = (float)model.angularDrag;
                if (!model.angularDragPressureOverride)
                {
                    num6 *= (float)num3 * PhysicsSettings.AngularDragMultiplier;
                }
                if ((double)(num6 * num6) > PhysicsSettings.PHYSX_RB_SQR_MAG_THRESHOLD)
                {
                    activeRigidBody.angularDrag = num6;
                }
                else
                {
                    activeRigidBody.angularDrag = 0f;
                }
                float num7 = (float)model.drag;
                if ((double)(num7 * num7) > PhysicsSettings.PHYSX_RB_SQR_MAG_THRESHOLD)
                {
                    activeRigidBody.drag = num7;
                }
                else
                {
                    activeRigidBody.drag = 0f;
                }
            }

            Physics.autoSyncTransforms = true;

            //if (!rbb.IsPhysXActive)
            //{
            //    return;
            //}

            // Seems to be enabled, but not sure if it's necessary.
            /*
            if (PhysicsSettings.ENABLE_INERTIA_TENSOR_SCALING && part != null)
            {
                MassScaleType massScaleType = (PhysicsSettings.ENABLE_DYNAMIC_TENSOR_SOLUTION ? _dynamicMassScaleType : _massScaleType);
                if (massScaleType != _dynamicMassScaleType && _simObjectComponent != null)
                {
                    PartComponent part3 = _simObjectComponent.SimulationObject.Part;
                    if (part3 != null)
                    {
                        PartOwnerComponent partOwner = part3.PartOwner;
                        if (partOwner != null && partOwner.PartCount == 1 && partOwner.RootPart == part3)
                        {
                            if (mass <= PhysicsSettings.GLOBAL_LOWMASS_TENSOR_LIMIT)
                            {
                                massScaleType = MassScaleType.Explicit;
                                _massScaleFactor = PhysicsSettings.GLOBAL_LOWMASS_TENSOR_SCALAR;
                            }
                            else
                            {
                                massScaleType = MassScaleType.None;
                            }
                        }
                    }
                }
                switch (massScaleType)
                {
                    case MassScaleType.InverseMass:
                        {
                            float num10 = PhysicsSettings.GLOBAL_TENSOR_SCALAR;
                            if (!Mathf.Approximately(_globalTensorScalingOverride, num10))
                            {
                                num10 = _globalTensorScalingOverride;
                            }
                            _massScaleFactor = num10 / activeRigidBody.mass;
                            ScaleInertiaTensor(activeRigidBody, _massScaleFactor);
                            break;
                        }
                    case MassScaleType.InverseMassDifferential:
                        {
                            Joint component2 = GetComponent<Joint>();
                            if (component2 != null)
                            {
                                Rigidbody connectedBody2 = component2.connectedBody;
                                if (connectedBody2 != null)
                                {
                                    float num11 = (_massScaleFactor = connectedBody2.mass / activeRigidBody.mass);
                                    ScaleInertiaTensor(activeRigidBody, _massScaleFactor);
                                }
                            }
                            else if (_isUnscaledInertiaTensorInitialized)
                            {
                                ResetInertiaTensor();
                                _isUnscaledInertiaTensorInitialized = false;
                            }
                            break;
                        }
                    case MassScaleType.Explicit:
                        ScaleInertiaTensor(activeRigidBody, _massScaleFactor);
                        break;
                    case MassScaleType.None:
                        if (_isUnscaledInertiaTensorInitialized)
                        {
                            ResetInertiaTensor();
                            _isUnscaledInertiaTensorInitialized = false;
                        }
                        break;
                }
            }
            else if (_isUnscaledInertiaTensorInitialized)
            {
                ResetInertiaTensor();
                _isUnscaledInertiaTensorInitialized = false;
            }
            */
        }
    }
}
