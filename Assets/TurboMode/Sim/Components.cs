using AwesomeTechnologies.Shaders;
using KSP.Sim;
using KSP.Sim.impl;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace TurboMode.Sim
{

    public struct Vessel : ISharedComponentData, IEquatable<Vessel>
    {
        public IGGuid guid;

        public Vessel(SimulationObjectModel obj)
        {
            guid = obj.Part.PartOwner.SimulationObject.Vessel.GlobalId;
        }

        public readonly bool Equals(Vessel other) => guid == other.guid;
        public override int GetHashCode() => guid.GetHashCode();
    }

    public struct Part : IComponentData
    {
        public double dryMass;

        public Part(SimulationObjectModel obj)
        {
            dryMass = obj.Part.DryMass;
        }
    }

    public struct SimObject : IComponentData
    {
        public IGGuid guid;
        public double utCreationTime;
        //public Position position;
        //public Rotation rotation;
        // type flags enum?

        public SimObject(SimulationObjectModel obj)
        {
            guid = obj.GlobalId;
            utCreationTime = obj.UTCreationTime;
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
}
