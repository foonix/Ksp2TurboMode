using KSP.Game;
using KSP.Logging;
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

            gameObject.SetActive(false); // suppress Update() overhead
        }
    }
}
