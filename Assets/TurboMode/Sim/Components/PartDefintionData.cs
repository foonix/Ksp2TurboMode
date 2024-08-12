using KSP.Game;
using KSP.Sim.Definitions;
using System;
using System.Collections.Generic;
using Unity.Entities;

namespace TurboMode.Sim.Components
{
    /// <summary>
    /// Singleton that stores data in common for a given type of part that doesn't change for a particular part instance.
    /// </summary>
    [InternalBufferCapacity(500)]
    public readonly struct PartDefintionData : IBufferElementData
    {
        /// <summary>
        /// The base mass of the part. Not counting module mass (procedural parts), resource mass, or "green mass"
        /// </summary>
        public readonly double mass;
        public readonly double maxTemp;
        public readonly double heatConductivity;
        public readonly Flags flags;

        [Flags]
        public enum Flags
        {
            None = 0,
            IsCompund = 1,
            BuoyancyUseSine = 2,
            FuelCrossFeed = 4,
        }

        public PartDefintionData(PartData orig)
        {
            mass = orig.mass;
            maxTemp = orig.maxTemp;
            heatConductivity = orig.heatConductivity;

            flags = default;
        }

        /// <summary>
        /// Maps part names (string keys for the type of part) to IDs for use inside of ECS.
        /// </summary>
        public class PartNameToDataIdMap : IComponentData
        {
            public readonly Dictionary<string, ushort> map = new(400);
        }

        public static Entity BuildDbSingleton(EntityManager em)
        {
            var rtddb = em.CreateSingletonBuffer<PartDefintionData>("PartTypeDataDb");
            var buffer = em.GetBuffer<PartDefintionData>(rtddb, false);

            var rtddbMap = em.CreateSingleton<PartNameToDataIdMap>("PartTypeDataDbMap");
            PartNameToDataIdMap mapComponent = new();
            em.SetComponentData(rtddbMap, mapComponent);
            var map = mapComponent.map;

            var partsProvider = GameManager.Instance.Game.Parts;

            foreach (PartCore part in partsProvider.AllParts())
            {
                map.Add(part.data.partName, (ushort)buffer.Length);
                buffer.Add(new PartDefintionData(part.data));
            }

            return rtddb;
        }
    }
}
