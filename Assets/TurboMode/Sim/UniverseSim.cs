using KSP.Game;
using KSP.Sim.impl;
using System;
using System.Collections.Generic;
using TurboMode.Sim.Components;
using TurboMode.Sim.Systems;
using Unity.Entities;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace TurboMode.Sim
{
    public class UniverseSim : IPriorityOverride, IFixedUpdate, IDisposable
    {
        public int ExecutionPriorityOverride => 3;

        readonly UniverseModel universeModel;
        readonly World world;
        readonly EntityManager em; // just for shorthand
        readonly GameInstance gameInstance;

        static readonly ProfilerMarker UniverseSim_OnFixedUpdate = new("UniverseSim.OnFixedUpdate");

        readonly Dictionary<IGGuid, Entity> simToEnt = new();

        public UniverseSim(GameInstance gameInstance)
        {
            this.gameInstance = gameInstance;
            universeModel = gameInstance.UniverseModel;
            universeModel.onVesselAdded += OnVesselAdded;
            gameInstance.RegisterFixedUpdate(this);

            world = new World("Ksp2TurboMode", WorldFlags.None);
            em = world.EntityManager;
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
            var resourceSystem = world.GetExistingSystemManaged<ResourceManagementSystem>();
            resourceSystem.Update();
            var rigidbodySystem = world.GetExistingSystemManaged<RigidbodySystem>();
            rigidbodySystem.Update();
        }

        private void InitSystems()
        {
            var simUpdateGroup = world.GetOrCreateSystemManaged<SimUpdateGroup>();

            var refreshFromUniverse = world.CreateSystemManaged<RefreshFromUniverse>();
            simUpdateGroup.AddSystemToUpdateList(refreshFromUniverse);

            world.CreateSystemManaged<RigidbodySystem>();
            world.CreateSystemManaged<ResourceManagementSystem>();

            // Not using default groups yet, but set them up anyway.  Maybe someone will want them?
            var playerLoop = PlayerLoop.GetCurrentPlayerLoop();
            ScriptBehaviourUpdateOrder.AppendSystemToPlayerLoop(simUpdateGroup, ref playerLoop, typeof(FixedUpdate));
            PlayerLoop.SetPlayerLoop(playerLoop);

            ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop(world);
        }

        public Entity AddSimObj(SimulationObjectModel obj)
        {
            Entity entity = em.CreateEntity(typeof(SimObject));
            em.SetComponentData(entity, new SimObject(obj));
            simToEnt[obj.GlobalId] = entity;

            foreach (var component in obj.Components)
            {
                AddComponent(entity, component);
            }

            return entity;
        }

        public void AddComponent(Entity entity, ObjectComponent component)
        {
            switch (component)
            {
                case PartComponent part:
                    em.AddComponent<Part>(entity);
                    em.SetComponentData<Part>(entity, new(part));
                    break;
                case VesselComponent vessel:
                    em.AddComponent<Vessel>(entity);
                    break;
                case KSP.Sim.impl.RigidbodyComponent rbc:
                    em.AddComponent<Sim.Components.RigidbodyComponent>(entity);
                    em.SetComponentData<Sim.Components.RigidbodyComponent>(entity, new());
                    break;
            }
        }

        public void ChangeOwner(IGGuid simObj, Entity to)
        {
            var entity = simToEnt[simObj];
            var simObjData = em.GetComponentData<SimObject>(entity);
            simObjData.owner = to;
            em.SetComponentData(entity, simObjData);
        }
    }
}
