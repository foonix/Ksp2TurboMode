using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using MonoMod.RuntimeDetour;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Burst;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TurboMode
{
    [BepInPlugin("TurboMode", "TurboMode", "0.2.2.0")]
    public class TurboModePlugin : BaseUnityPlugin
    {
        // If the file extension is .dll, SpaceWarp and BepInEx will log exceptions.
        public static readonly string burstCodeAssemblyName = "TurboMode_win_x86_64.dll_IGNOREME";

        private static readonly List<IDetour> hooks = new();

        // Disable game state interactions, and enable verification those would have done the right thing.
        internal static bool testModeEnabled = false;
        public static bool enableVesselSelfCollide = true;
        public static bool enableEcsSim = false;

        public ConfigEntry<bool> testModeConfig;
        public ConfigEntry<bool> enableVesselSelfCollideConfig;
        public ConfigEntry<bool> enableSelectivePhysicsSync;
        public ConfigEntry<bool> enableEcsSimConfig;

        private void Awake()
        {
            Logger.LogInfo($"TurboMode startup sequence initiated");

            testModeConfig = Config.Bind(
                "General",
                "TestMode",
                false,
                "Disable game state interactions, and enable verification those would have done the right thing."
            );
            enableVesselSelfCollideConfig = Config.Bind(
                "General",
                "EnableVesselSelfCollideOptimization",
                true,
                "Speeds up loading/dock/undock operations for large part count vessels"
            );
            enableSelectivePhysicsSync = Config.Bind(
                "General",
                "EnableSelectivePhysicsSync",
                true,
                "Disables Unity's Physics.autoSyncTransforms feature for certain operations unlikely to need it."
            );
            enableEcsSimConfig = Config.Bind(
                "General",
                "EnableEcsSimulation",
                false,
                "Use Unity ECS for simulation (\"background\") updates.  DOES NOT WORK YET."
            );

            testModeEnabled = testModeConfig.Value;
            enableVesselSelfCollide = enableVesselSelfCollideConfig.Value;
            enableEcsSim = enableEcsSimConfig.Value;

            SceneManager.sceneLoaded += (scene, mode) =>
            {
                Logger.Log(LogLevel.Info, $"TM: Scene loaded {scene.name}");
                if (scene.name == "boot-ksp")
                    new GameObject("TurboModeBootstrap", typeof(Behaviors.TurboModeBootstrap));
            };

            if (enableVesselSelfCollideConfig.Value)
                hooks.AddRange(CollisionManagerPerformance.MakeHooks());
            if (enableSelectivePhysicsSync.Value)
                hooks.AddRange(SelectivePhysicsAutoSync.MakeHooks());
            hooks.AddRange(AdditionalProfilerTags.MakeHooks());

            var cwd = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            Assembly.LoadFile(Path.Combine(cwd, "Unity.Entities.dll"));

            var burstLibFullpath = Path.GetFullPath(Path.Combine(cwd, burstCodeAssemblyName));
            if (!File.Exists(burstLibFullpath))
            {
                Logger.LogError($"Can't find burst assembly at {burstLibFullpath}");
            }

            bool burstLoaded = BurstRuntime.LoadAdditionalLibrary(burstLibFullpath);
            if (!burstLoaded)
            {
                Logger.LogError($"BurstRuntime failed to load assembly at {burstLibFullpath}");
            }
        }
    }
}
