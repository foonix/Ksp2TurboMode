using KSP.Sim.impl;
using System.Collections.Generic;
using Unity.Entities;

namespace TurboMode.Sim.Components
{
    // components too small to deserve their own file

    public struct Vessel : IComponentData
    {
        public Vector3d gravityAtCurrentLocation;
    }

    public struct Part : IComponentData
    {
        public double dryMass;

        public Part(SimulationObjectModel obj)
        {
            dryMass = obj.Part.DryMass;
        }

        public Part(PartComponent part)
        {
            dryMass = part.DryMass;
        }
    }

    public struct SimObject : IComponentData
    {
        public IGGuid guid;
        public Entity owner;
        public double utCreationTime;
        //public Position position;
        //public Rotation rotation;
        // type flags enum?

        public SimObject(SimulationObjectModel obj)
        {
            guid = obj.GlobalId;
            utCreationTime = obj.UTCreationTime;
            owner = default;
        }
    }

    public struct Resource
    {

    }

    public struct ResourceContainer : IComponentData
    {
        //public NativeList<Resource> resources;
    }

    public class UniverseRef : IComponentData
    {
        public UniverseModel universeModel;
        public readonly Dictionary<IGGuid, Entity> simGuidToEntity = new();
    }

    public struct RigidbodyComponent : IComponentData
    {
        public Vector3d accelerations;
    }
}
