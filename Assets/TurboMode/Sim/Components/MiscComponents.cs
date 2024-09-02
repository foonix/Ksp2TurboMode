using KSP.Sim;
using KSP.Sim.impl;
using System.Collections.Generic;
using Unity.Entities;

namespace TurboMode.Sim.Components
{
    // components too small to deserve their own file

    public struct Vessel : IComponentData
    {
        public Vector3d gravityAtCurrentLocation;
        /// <summary>
        /// Center of mass of a vessel relative to it's PartOwnerComponent, which lives on the root object
        /// </summary>
        public Vector3d centerOfMass;
        public Vector3d angularVelocityMassAvg;
        public Vector3d velocityMassAvg;
        public Vector3d angularMomentum;
        public Vector3d momentOfInertia;
        public double totalMass;
        public double reEntryMaximumFlux;
    }

    public struct OwnedPartRef : IBufferElementData
    {
        public Entity partEntity;

        public static void EnsureRemoved(DynamicBuffer<OwnedPartRef> childBuffer, Entity entity)
        {
            for (int i = 0; i < childBuffer.Length; i++)
            {
                while (childBuffer[i].partEntity == entity)
                {
                    childBuffer.RemoveAtSwapBack(i);
                }
            }
        }

        public static void EnsureContains(DynamicBuffer<OwnedPartRef> childBuffer, Entity entity)
        {
            foreach (var child in childBuffer)
            {
                if (child.partEntity == entity) return;
            }
            childBuffer.Add(new OwnedPartRef() { partEntity = entity });
        }
    }

    public struct Part : IComponentData
    {
        readonly public ushort typeId;

        public PartPhysicsModes physicsMode;

        public Matrix4x4D localToOwner;
        /// <summary>
        /// Center of mass is dynamic on some parts (seems just control surfaces)
        /// Position is relative to sim object.
        /// </summary>
        public Vector3d centerOfMass;
        public Vector3d velocity;
        public Vector3d angularVelocity;
        public Vector3d inertiaTensor;
        public QuaternionD inertiaTensorRotation;


        public double dryMass;
        public double greenMass;
        public double wetMass;

        public double reEntryMaximumFlux;

        public Part(PartComponent part, ushort typeId)
        {
            this.typeId = typeId;
            physicsMode = default;
            dryMass = part.DryMass;
            greenMass = part.GreenMass;
            wetMass = part.WetMass;
            localToOwner = Matrix4x4D.Identity();
            centerOfMass = default;
            velocity = default;
            angularVelocity = default;
            inertiaTensor = default;
            inertiaTensorRotation = QuaternionD.identity;
            reEntryMaximumFlux = 0;
        }
    }

    public class SimObject : IComponentData
    {
        public IGGuid guid;
        public Entity owner;
        public double utCreationTime;
        public SimulationObjectModel inUniverse;
        //public Position position;
        //public Rotation rotation;
        // type flags enum?

        public SimObject() { }

        public SimObject(SimulationObjectModel obj)
        {
            guid = obj.GlobalId;
            utCreationTime = obj.UTCreationTime;
            owner = default;
            inUniverse = obj;
        }
    }

    public class ViewObjectRef : IComponentData
    {
        public SimulationObjectView view;
    }

    public class UniverseRef : IComponentData
    {
        public UniverseModel universeModel;
        public readonly Dictionary<IGGuid, Entity> simGuidToEntity = new();
    }

    public struct RigidbodyComponent : IComponentData
    {
        public Vector3d accelerations;
        public double effectiveMass;
    }

    public struct KerbalStorage : IComponentData
    {
        public ushort count;
    }

    /// <summary>
    /// Sum of mass adjustments from PartComponent.Modules.ValuesList[i].DataModules.Values where Value is IMassModifier
    /// </summary>
    public struct MassModifiers : IComponentData
    {
        public double mass;
    }
}
