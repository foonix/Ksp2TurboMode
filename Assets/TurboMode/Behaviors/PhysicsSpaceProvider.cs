using KSP.Api;
using KSP.Sim;
using KSP.Sim.impl;
using Unity.Profiling;
using UnityEngine;

namespace TurboMode.Behaviors
{
    [DisallowMultipleComponent]
    public class PhysicsSpaceProvider : MonoBehaviour, IPhysicsSpaceProvider
    {
        KSP.Sim.impl.PhysicsSpaceProvider orig;
        InertialFrame _inertialReferenceFrame;

        private void Awake()
        {
            orig = GetComponent<KSP.Sim.impl.PhysicsSpaceProvider>();
            _inertialReferenceFrame = orig.GetField<KSP.Sim.impl.PhysicsSpaceProvider, InertialFrame>("_inertialReferenceFrame");
        }

        public FloatingOrigin FloatingOrigin => orig.FloatingOrigin;

        public ITransformFrame ReferenceFrame => orig.ReferenceFrame;

        public Vector3d AngularVelocityToPhysics(AngularVelocity angularVelocity)
        {
            return orig.AngularVelocityToPhysics(angularVelocity);
        }

        public Vector3d GetGravityForceAtPosition(Position pos)
        {
            return orig.GetGravityForceAtPosition(pos);
        }

        public AngularVelocity PhysicsToAngularVelocity(Vector3d physicsSpaceAngularVelocity)
        {
            return orig.PhysicsToAngularVelocity(physicsSpaceAngularVelocity);
        }

        public Position PhysicsToPosition(Vector3d physicsSpacePosition)
        {
            return orig.PhysicsToPosition(physicsSpacePosition);
        }

        public Vector3d PhysicsToPosition(Vector3d physicsSpacePosition, ICoordinateSystem outputFrame)
        {
            return orig.PhysicsToPosition(physicsSpacePosition, outputFrame);
        }

        public Rotation PhysicsToRotation(QuaternionD physicsSpaceRotation)
        {
            return orig.PhysicsToRotation(physicsSpaceRotation);
        }

        public QuaternionD PhysicsToRotation(QuaternionD physicsSpaceRotation, ICoordinateSystem outputFrame)
        {
            return orig.PhysicsToRotation(physicsSpaceRotation, outputFrame);
        }

        public Vector PhysicsToVector(Vector3d physicsSpaceVector)
        {
            return orig.PhysicsToVector(physicsSpaceVector);
        }

        public Vector3d PhysicsToVector(Vector3d physicsSpaceVector, ICoordinateSystem outputFrame)
        {
            return orig.PhysicsToVector(physicsSpaceVector, outputFrame);
        }

        public Velocity PhysicsToVelocity(Vector3d physicsSpaceVelociy)
        {
            return orig.PhysicsToVelocity(physicsSpaceVelociy);
        }

        public Vector3d PositionToPhysics(Position position)
        {
            return orig.PositionToPhysics(position);
        }

        public Vector3d PositionToPhysics(ICoordinateSystem referenceFrame, Vector3d localPosition)
        {
            return orig.PositionToPhysics(referenceFrame, localPosition);
        }

        public QuaternionD RotationToPhysics(Rotation rotation)
        {
            return orig.RotationToPhysics(rotation);
        }

        public QuaternionD RotationToPhysics(ICoordinateSystem referenceFrame, QuaternionD localRotation)
        {
            return orig.RotationToPhysics(referenceFrame, localRotation);
        }

        public void SetReferenceFrame(ITransformFrame referenceFrame)
        {
            orig.SetReferenceFrame(referenceFrame);
        }

        private static readonly ProfilerMarker s_VectorToPhysics_Vector = new("TM: VectorToPhysics(Vector)");
        public Vector3d VectorToPhysics(Vector vector)
        {
            s_VectorToPhysics_Vector.Begin();

            // Assumptions:
            // _inertialReferenceFrame.inertialReferenceFrame is never not a NonRotatingFrame for this service.
            // It's never our reference frame on the vector.

            var frame = _inertialReferenceFrame.inertialReferenceFrame as NonRotatingFrame;
            var vectorCoordinateSystem = vector.coordinateSystem;
            var vector3d = vector.vector;

            if (frame == vectorCoordinateSystem)
            {
                s_VectorToPhysics_Vector.End();
                return vector3d;
            }

            //var result = frame.ToLocalVector(vectorCoordinateSystem, vector3d);

            //Matrix4x4D mostRecentInverseMatrix = frame.ComputeTransformFromOther(vectorCoordinateSystem);
            Matrix4x4D mostRecentInverseMatrix = MathUtil.ComputeTransformFromOther(frame, vectorCoordinateSystem);
            Vector3d result = mostRecentInverseMatrix.TransformVector(vector3d);

            s_VectorToPhysics_Vector.End();
            return result;
        }

        private static readonly ProfilerMarker s_VectorToPhysics_Framed = new("TM: VectorToPhysics(Framed)");
        public Vector3d VectorToPhysics(ICoordinateSystem referenceFrame, Vector3d localPosition)
        {
            s_VectorToPhysics_Framed.Begin();
            var result = orig.VectorToPhysics(referenceFrame, localPosition);
            s_VectorToPhysics_Framed.End();
            return result;
        }

        public Vector3d VelocityToPhysics(Velocity velocity, Position whereIsIt)
        {
            return orig.VelocityToPhysics(velocity, whereIsIt);
        }
    }
}