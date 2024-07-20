using BepInEx;
using BepInEx.Logging;
using KSP.Logging;
using MonoMod.RuntimeDetour;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TurboMode
{
    [BepInPlugin("TurboMode", "TurboMode", "0.2.2.0")]
    public class TurboModePlugin : BaseUnityPlugin
    {
        private static readonly List<IDetour> hooks = new();

        // Disable game state interactions, and enable verification those would have done the right thing.
        internal static bool testMode = false;

        private void Awake()
        {
            Logger.LogInfo($"TurboMode startup sequence initiated");
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                Logger.Log(LogLevel.Info, $"TM: Scene loaded {scene.name}");
                if (scene.name == "boot-ksp")
                    new GameObject("TurboModeBootstrap", typeof(Behaviors.TurboModeBootstrap));
            };

            hooks.AddRange(CollisionManagerPerformance.MakeHooks());
            hooks.AddRange(AdditionalProfilerTags.MakeHooks());

            // not sure how this gets enabled debug mode, but it's a huge amount of time for each message
            // Their logger api defaults to debug logging.
            GlobalLog.DisableFilter(LogFilter.Debug | LogFilter.General | LogFilter.Simulation);
        }
    }
}
