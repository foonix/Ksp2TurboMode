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
        UniverseSim universeSim;

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

                gameInstance.Messages.Subscribe<GameLoadFinishedMessage>((message) =>
                {
                    Debug.Log($"TM: Game load finished");
                });

                messageCenter = gameInstance.Messages;
                // have to be a child of something that GameManger won't destroy during CoroutineDestroyAll()
                transform.parent = GameManager.Instance.transform;
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

            // Wait for UniverseSim dependencies during initial game load.
            // Dispose and re-init UniverseSim on subsequent game reloads.
            if (TurboModePlugin.enableEcsSim && initialized
                && gameInstance.SpaceSimulation != spaceSim && gameInstance.SpaceSimulation is not null
                && GameManager.Instance.Game.Parts.IsDataLoaded)
            {
                ResetUniverseSim();
            }
        }

        private void ResetUniverseSim()
        {
            Debug.Log("TM: Resetting UniverseSim");
            universeSim?.Dispose();
            spaceSimMonitor?.Dispose();
            spaceSim = gameInstance.SpaceSimulation;
            universeSim = new UniverseSim(GameManager.Instance.Game);
            spaceSimMonitor = new SpaceSimulationMonitor(spaceSim, universeSim);
        }

        private void OnDestroy()
        {
            Debug.Log("TM: Bootstrap destroyed!");
        }
    }
}
