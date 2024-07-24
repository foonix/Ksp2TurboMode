using KSP.Game;
using KSP.Logging;
using KSP.Messages;
using UnityEngine;

namespace TurboMode.Behaviors
{
    public class TurboModeBootstrap : MonoBehaviour
    {
        GameInstance gameInstance;

        private void Start()
        {
            Debug.Log("TM: bootstrap starting");
            DontDestroyOnLoad(gameObject);
        }

        void Update()
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

            CollisionManagerPerformance.OnGameInstanceInitialized(gameInstance);

            if (!TurboModePlugin.testModeEnabled)
            {
                GlobalLog.DisableFilter(LogFilter.Debug | LogFilter.General | LogFilter.Simulation);
            }

            gameInstance.Messages.Subscribe<GameLoadFinishedMessage>((message) =>
            {
                Debug.Log($"TM: Game load finished");
                new UniverseSim(GameManager.Instance.Game);
            });

            gameObject.SetActive(false); // suppress Update() overhead
        }
    }
}
