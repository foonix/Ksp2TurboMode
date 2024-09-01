using KSP.Game;
using KSP.Modding;
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
        readonly Hook partOwnerComponentUpdateMassStatsShutoff = new(
                typeof(PartOwnerComponent).GetMethod("UpdateMassStats"),
                (Action<Action<object>, object>)SuppressionUtils.VoidShutoff
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

            // replaces PartOwnerComponent.UpdateMassStats(), which runs once per frame
            // (dirty flag reset during LateUpdate).  They run out of different call chains
            // depending on if the vessel has an active RigidbodyBehavior or not, but they always
            // run at least once during FixedUpdate anyway, so we frontload it here.
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

                    var rigidbodyComponet = simObj.inUniverse.Rigidbody;
                    rigidbodyComponet.mass = (float)rbc.effectiveMass;
                    rigidbodyComponet.centerOfMass = partComponent.CenterOfMass;
                })
                .WithoutBurst()
                .Run();

            Entities
                .WithName("UpdateVesselGravity")
                .ForEach(
                (ref Vessel vessel, in SimObject simObj) =>
                {
                    // cache vessel gravity for active physic parts
                    var vesselObj = simObj.inUniverse;
                    if (vesselObj.objVesselBehavior)
                    {
                        vessel.gravityAtCurrentLocation = vesselObj.objVesselBehavior.PartOwner.GetGravityForceAtCurrentPosition();
                    }
                    // TODO: drive whatever is running the vessel mass update
                })
                .WithoutBurst()
                .Run();

            Entities
                .WithName("UpdateRbForcesVessel")
                .ForEach(
                (ref Components.RigidbodyComponent rbc, in Vessel vessel, in ViewObjectRef viewObj) =>
                {
                    rbc.accelerations = vessel.gravityAtCurrentLocation;

                    var sim = GameManager.Instance.Game.SpaceSimulation;
                    var rbb = viewObj.view.Rigidbody;

                    if (!rbb.activeRigidBody)
                    {
                        return;
                    }

                    s_RbbFixedUpdate.Begin(rbb);
                    UpdateRbForces(rbb, sim.UniverseModel, rbc.accelerations);

                    var simObj = viewObj.view.Model;
                    // This will replace most of what UpdateRbForces() is doing.
                    //owner.SetField("_isPhysicsStatsDirty", false);
                    //RefactorRigidbodyBehavior.UpdateToSimObject(rbb);
                    UpdatePhysicsStats(in vessel, simObj);
                    _isHandCorrectionCheckPendingField.Set(rbb, true);
                    s_RbbFixedUpdate.End();
                })
                .WithoutBurst()
                .Run();

            Entities
                .WithName("UpdateRbForces")
                .WithAbsent<Vessel>()
                .ForEach(
                (ref Components.RigidbodyComponent rbc, in SimObject simObj, in ViewObjectRef viewObj) =>
                {
                    var sim = GameManager.Instance.Game.SpaceSimulation;
                    if (vesselLookup.TryGetComponent(simObj.owner, out var vessel))
                    {
                        rbc.accelerations = vessel.gravityAtCurrentLocation;
                    }
                    else
                    {
                        Position position = simObj.inUniverse.transform.Position;
                        rbc.accelerations = sim.UniverseView.PhysicsSpace.GetGravityForceAtPosition(position);
                    }

                    var rbb = viewObj.view.Rigidbody;

                    if (!rbb.activeRigidBody)
                    {
                        return;
                    }

                    s_RbbFixedUpdate.Begin(rbb);
                    UpdateRbForces(rbb, sim.UniverseModel, rbc.accelerations);
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
        private static void UpdateRbForces(RigidbodyBehavior rbb, UniverseModel universeModel, Vector3d gravity)
        {
            var model = rbb.Model;
            var activeRigidBody = rbb.activeRigidBody;
            var localToWorldMatrix = rbb.transform.localToWorldMatrix;
            var part = rbb.ViewObject.Part;
            var vessel = rbb.ViewObject.Vessel;
            var rbbIsEnabled = rbb.enabled;

            if (rbbIsEnabled)
                RefactorRigidbodyBehavior.UpdateToSimObject(rbb);

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

            if (!rbb.IsPhysXActive)
            {
                return;
            }

            RefactorRigidbodyBehavior.UpdateTesorScale(model, rbb);
        }

        private static void UpdatePhysicsStats(in Vessel vessel, SimulationObjectModel vesselSimObj)
        {
            var partOwnerComponent = vesselSimObj.PartOwner;
            var rbc = vesselSimObj.Rigidbody;
            var bodyframe = partOwnerComponent.transform.bodyFrame;
            var physicsSpace = GameManager.Instance.Game.UniverseView.PhysicsSpace;

            var comPosition = new Position(bodyframe, vessel.centerOfMass);

            partOwnerComponent.SetProperty("TotalMass", vessel.totalMass);
            partOwnerComponent.CenterOfMass = comPosition;
            partOwnerComponent.SetProperty("AngularVelocityMassAvg", physicsSpace.PhysicsToAngularVelocity(vessel.angularVelocityMassAvg));
            partOwnerComponent.SetProperty("VelocityMassAvg", physicsSpace.PhysicsToVelocity(vessel.velocityMassAvg));

            // Still needed: MOI and AngularMomentum

            rbc.centerOfMass = comPosition;
            rbc.mass = (float)vessel.totalMass;
        }

    }
}
