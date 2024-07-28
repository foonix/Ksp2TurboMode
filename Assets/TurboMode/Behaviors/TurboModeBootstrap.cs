using KSP.Game;
using KSP.Logging;
using KSP.Messages;
using KSP.Sim.impl;
using TurboMode.Sim;
using UnityEngine;

namespace TurboMode.Behaviors
{
    public class TurboModeBootstrap : MonoBehaviour
    {
        GameInstance gameInstance;
        MessageCenter messageCenter;

        SpaceSimulation spaceSim;
        SpaceSimulationMonitor spaceSimMonitor;

        bool initialized;

        private void Start()
        {
            Debug.Log("TM: bootstrap starting");
            DontDestroyOnLoad(gameObject);
        }

        void Update()
        {
            if (!initialized)
            {
                if (!GameManager.Instance || !GameManager.Instance.Game)
                {
                    return;
                }

                gameInstance = GameManager.Instance.Game;

                if (!gameInstance.IsInitialized)
                {
                    return;
                }

                Debug.Log("TM: bootstrapping gameInstance events");

                if (TurboModePlugin.enableVesselSelfCollide)
                {
                    CollisionManagerPerformance.OnGameInstanceInitialized(gameInstance);
                }

                if (!TurboModePlugin.testModeEnabled)
                {
                    GlobalLog.DisableFilter(LogFilter.Debug | LogFilter.General | LogFilter.Simulation);
                }

                gameInstance.Messages.Subscribe<GameLoadFinishedMessage>((message) =>
                {
                    Debug.Log($"TM: Game load finished");
                    if (TurboModePlugin.enableEcsSim)
                    {
                        new UniverseSim(GameManager.Instance.Game);
                    }
                });

                messageCenter = gameInstance.Messages;
                initialized = true;
            }
            else
            {
                if (!gameInstance)
                {
                    Debug.Log($"TM: Game instance is gone");
                }
                if (gameInstance.Messages != messageCenter)
                {
                    Debug.Log($"TM: Message center changed");
                }
            }

            if (initialized && gameInstance.SpaceSimulation != spaceSim && gameInstance.SpaceSimulation is not null)
            {
                spaceSimMonitor = new SpaceSimulationMonitor(gameInstance.SpaceSimulation);
                spaceSim = gameInstance.SpaceSimulation;
            }
        }
    }
}
