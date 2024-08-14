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
        readonly public ushort typeId;
        public double dryMass;
        public double greenMass;
        public double wetMass;

        public Part(PartComponent part, ushort typeId)
        {
            this.typeId = typeId;
            dryMass = part.DryMass;
            greenMass = part.GreenMass;
            wetMass = part.WetMass;
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
