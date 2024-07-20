using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
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
        internal static bool testModeEnabled = false;
        public ConfigEntry<bool> testMode;

        private void Awake()
        {
            Logger.LogInfo($"TurboMode startup sequence initiated");

            testMode = Config.Bind("General",
                                    "TestMode",
                                    false,
                                    "Disable game state interactions, and enable verification those would have done the right thing.");
            testModeEnabled = testMode.Value;

            SceneManager.sceneLoaded += (scene, mode) =>
            {
                Logger.Log(LogLevel.Info, $"TM: Scene loaded {scene.name}");
                if (scene.name == "boot-ksp")
                    new GameObject("TurboModeBootstrap", typeof(Behaviors.TurboModeBootstrap));
            };

            hooks.AddRange(CollisionManagerPerformance.MakeHooks());
            hooks.AddRange(AdditionalProfilerTags.MakeHooks());
        }
    }
}
