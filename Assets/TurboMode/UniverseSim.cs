using KSP.Game;
using KSP.Sim.impl;
using System;
using System.Collections;
using System.Collections.Generic;
using TurboMode.Sim;
using Unity.Entities;
using Unity.Profiling;
using UnityEngine;

namespace TurboMode
{
    public class UniverseSim : IFixedUpdate, IDisposable
    {
        readonly UniverseModel universeModel;
        readonly World world;

        

        static readonly ProfilerMarker UniverseSim_OnFixedUpdate = new("UniverseSim.OnFixedUpdate");

        public UniverseSim(GameInstance gameInstance)
        {
            universeModel = gameInstance.UniverseModel;
            universeModel.onVesselAdded += OnVesselAdded;
            try
            {
                // The way Burst stores compiled code causes exceptions if it
                // can't find the compiled code in lib_burst_generated.dll
                // So turn off burst comiple but only when running our own stuff.
                //Unity.Burst.BurstCompiler.Options.EnableBurstCompilation = false;
                world = new World("Ksp2TurboMode");
                Debug.Log("TM: Entity world created");
                var modelRefEnt = world.EntityManager.CreateSingleton<UniverseRef>("UniverseModelRef");
                var modelRefData = new UniverseRef()
                {
                    universeModel = universeModel,
                };
                world.EntityManager.SetComponentData(modelRefEnt, modelRefData);
                InitSystems();
                InitFromExistingUniverse(modelRefData);
            }
            finally
            {
                //Unity.Burst.BurstCompiler.Options.EnableBurstCompilation = true;
            }

            Debug.Log("TM: World initialized");
        }

        public void Dispose() => world?.Dispose();

        ~UniverseSim() => Dispose();

        private void InitFromExistingUniverse(UniverseRef universeRef)
        {
            var em = world.EntityManager;
            foreach (SimulationObjectModel obj in universeModel.GetAllSimObjects())
            {
                Entity entity;
                if (obj.IsPart)
                {
                    entity = em.CreateEntity(typeof(Vessel), typeof(SimObject), typeof(Part));
                    em.SetSharedComponent(entity, new Vessel(obj));
                    em.SetComponentData(entity, new SimObject(obj));
                    em.SetComponentData(entity, new Part(obj));
                }
                else
                {
                    entity = em.CreateEntity(typeof(SimObject));
                    em.SetComponentData(entity, new SimObject(obj));
                }
                universeRef.simGuidToEntity[obj.GlobalId] = entity;
            }
        }

        private void OnVesselAdded(VesselComponent component)
        {
            Debug.Log($"TM: Vessel added {component.Name} {component.DisplayName}");
        }

        public void OnFixedUpdate(float deltaTime)
        {
            try
            {
                UniverseSim_OnFixedUpdate.Begin();
                //Unity.Burst.BurstCompiler.Options.EnableBurstCompilation = false;

                OnFixedUpdateImpl(deltaTime);
            }
            finally
            {
                //Unity.Burst.BurstCompiler.Options.EnableBurstCompilation = true;
                UniverseSim_OnFixedUpdate.End();
            }
        }

        private void OnFixedUpdateImpl(float deltaTime)
        {
            world.Update();
        }

        private void InitSystems()
        {
            world.CreateSystemManaged<RefreshFromUniverse>();
        }
    }
}
