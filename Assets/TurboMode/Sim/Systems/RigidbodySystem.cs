using KSP.Game;
using KSP.Sim;
using KSP.Sim.impl;
using MonoMod.RuntimeDetour;
using System;
using System.Reflection;
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
        readonly Hook rbbOnUpdateShutoff = new(
                typeof(RigidbodyBehavior).GetMethod("OnFixedUpdate"),
                (Action<Action<System.Object, float>, RigidbodyBehavior, float>)RbbFixedUpdateShunt
                );

        public static void RbbFixedUpdateShunt(Action<object, float> orig, RigidbodyBehavior rbb, float deltaTime) { }

        private static readonly ProfilerMarker s_RbbFixedUpdate = new("RigidbodySystem fixed update");

        protected override void OnUpdate()
        {
            var universeSim = SystemAPI.ManagedAPI.GetSingleton<UniverseRef>();
            var game = GameManager.Instance.Game;

            if (!game.IsSimulationRunning())
            {
                return;
            }
            var vesselLookup = GetComponentLookup<Vessel>(true);

            Entities
                .WithName("UpdateVesselGravity")
                .ForEach(
                (ref Vessel vessel, in SimObject simObj) =>
                {
                    // cache vessel gravity for active physic parts
                    var vesselObj = universeSim.universeModel.FindSimObject(simObj.guid);
                    if (vesselObj.objVesselBehavior)
                    {
                        vessel.gravityAtCurrentLocation = vesselObj.objVesselBehavior.PartOwner.GetGravityForceAtCurrentPosition();
                    }
                    // TODO: drive whatever is running the vessel mass update
                })
                .WithoutBurst()
                .Run();

            Entities
                .WithName("UpdateRbForces")
                .ForEach(
                (ref Components.RigidbodyComponent rbc, in SimObject simObj) =>
                {
                    var vessel = vesselLookup[simObj.owner];
                    rbc.accelerations = vessel.gravityAtCurrentLocation;

                    var rbObj = universeSim.universeModel.FindSimObject(simObj.guid);
                    var rbView = GameManager.Instance.Game.SpaceSimulation.ModelViewMap.FromModel(rbObj);

                    if (!rbView || !rbView.Rigidbody || !rbView.Rigidbody.activeRigidBody)
                    {
                        return;
                    }

                    var rbb = rbView.Rigidbody;
                    s_RbbFixedUpdate.Begin(rbb);
                    UpdateRbForces(rbb, universeSim.universeModel, rbc.accelerations);
                    _isHandCorrectionCheckPendingField.Set(rbb, true);
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
        private static readonly MethodInfo updateToSimObject
            = typeof(RigidbodyBehavior).GetMethod("UpdateToSimObject", BindingFlags.NonPublic | BindingFlags.Instance);
        private void UpdateRbForces(RigidbodyBehavior rbb, UniverseModel universeModel, Vector3d gravity)
        {
            var model = rbb.Model;
            var activeRigidBody = rbb.activeRigidBody;
            var localToWorldMatrix = rbb.transform.localToWorldMatrix;
            var part = rbb.ViewObject.Part;
            var vessel = rbb.ViewObject.Vessel;
            var rbbIsEnabled = rbb.enabled;

            s_RbbFixedUpdate.Begin(rbb);
            if (rbbIsEnabled)
                updateToSimObject.Invoke(rbb, null);
            s_RbbFixedUpdate.End();

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

            foreach(var force in model.Forces)
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
                else if (vessel)
                {
                    num3 = vessel.Model.DynamicPressure_kPa * 0.0098692326671601278;
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