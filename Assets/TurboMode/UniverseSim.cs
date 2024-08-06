using KSP.Game;
using KSP.Sim.impl;
using System;
using TurboMode.Sim;
using Unity.Entities;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace TurboMode
{
    public class UniverseSim : IFixedUpdate, IDisposable
    {
        readonly UniverseModel universeModel;
        readonly World world;
        readonly GameInstance gameInstance;

        static readonly ProfilerMarker UniverseSim_OnFixedUpdate = new("UniverseSim.OnFixedUpdate");

        public UniverseSim(GameInstance gameInstance)
        {
            this.gameInstance = gameInstance;
            universeModel = gameInstance.UniverseModel;
            universeModel.onVesselAdded += OnVesselAdded;
            gameInstance.RegisterFixedUpdate(this);

            world = new World("Ksp2TurboMode", WorldFlags.None);
            Debug.Log("TM: Entity world created");
            var modelRefEnt = world.EntityManager.CreateSingleton<UniverseRef>("UniverseModelRef");
            var modelRefData = new UniverseRef()
            {
                universeModel = universeModel,
            };
            world.EntityManager.SetComponentData(modelRefEnt, modelRefData);
            InitSystems();

            // prevent crash on quit
            Application.quitting += Dispose;

            Debug.Log("TM: World initialized");
        }

        public void Dispose()
        {
            if (gameInstance != null)
                gameInstance.UnregisterFixedUpdate(this);
            world?.Dispose();
            Application.quitting -= Dispose;
        }

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

                OnFixedUpdateImpl(deltaTime);
            }
            finally
            {
                UniverseSim_OnFixedUpdate.End();
            }
        }

        private void OnFixedUpdateImpl(float deltaTime)
        {
            world.Update();
        }

        private void InitSystems()
        {
            var simUpdateGroup = world.GetOrCreateSystemManaged<SimUpdateGroup>();

            var refreshFromUniverse = world.CreateSystemManaged<RefreshFromUniverse>();
            simUpdateGroup.AddSystemToUpdateList(refreshFromUniverse);

            var playerLoop = PlayerLoop.GetCurrentPlayerLoop();
            ScriptBehaviourUpdateOrder.AppendSystemToPlayerLoop(simUpdateGroup, ref playerLoop, typeof(FixedUpdate));
            PlayerLoop.SetPlayerLoop(playerLoop);

            ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop(world);
        }

        public Entity AddSimObj(SimulationObjectModel obj)
        {
            var em = world.EntityManager;
            Entity entity = em.CreateEntity(typeof(SimObject));
            em.SetComponentData(entity, new SimObject(obj));

            foreach (var component in obj.Components)
            {
                AddComponent(entity, component);
            }

            return entity;
        }

        public void AddComponent(Entity entity, ObjectComponent component)
        {
            var em = world.EntityManager;
            switch (component)
            {
                case PartComponent part:
                    em.AddComponent(entity, typeof(Part));
                    em.SetComponentData(entity, new Part(part));
                    break;
                case VesselComponent:
                    break;
            }
        }
    }
}
