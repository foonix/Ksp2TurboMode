using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using MonoMod.RuntimeDetour;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TurboMode.Patches;
using Unity.Burst;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TurboMode
{
    [BepInPlugin(pluginId, pluginName, pluginVersion)]
    public class TurboModePlugin : BaseUnityPlugin
    {
        public const string pluginName = "Turbo Mode";
        public const string pluginId = "TurboMode";
        public const string pluginVersion = "0.1.1";

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
        public ConfigEntry<bool> burstMath;
        public ConfigEntry<bool> enableEcsSimConfig;
        public ConfigEntry<bool> shutoffUnusedWindowHierarchies;
        public ConfigEntry<bool> enableFlowRequestOptimizations;
        public ConfigEntry<bool> miscCleanups;
        public ConfigEntry<bool> graphicsJobsSettings;

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
            burstMath = Config.Bind(
                "General",
                "BurstMath",
                true,
                "Use Unity Burst code to speed up certain math operations, such as floating origin and reference frame calculations."
            );
            shutoffUnusedWindowHierarchies = Config.Bind(
                "General",
                "ShutoffUnusedWindowHierarchies",
                true,
                "Completely deactivate non-visible UI windows. May cause slight startup jitter when opening them."
            );
            miscCleanups = Config.Bind(
                "General",
                "MiscCleanups",
                true,
                "Miscellaneous small garbage cleanups and performance improvements."
            );
            enableEcsSimConfig = Config.Bind(
                "General",
                "EnableEcsSimulation",
                false,
                "Use Unity ECS for simulation (\"background\") updates.  DOES NOT WORK YET."
            );
            enableFlowRequestOptimizations = Config.Bind(
                "General",
                "EnableFlowRequestOptimizations",
                true,
                "Improved code for vessel resource flow request processing and related processes."
            );
            graphicsJobsSettings = Config.Bind(
                "General",
                "EnableGraphicsJobs",
                true,
                "Enable Unity Graphics jobs settings in boot.config.  Speeds up camera rendering. Note: This setting requires TWO restarts to take effect or change."
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
            if (shutoffUnusedWindowHierarchies.Value)
                hooks.AddRange(ShutoffUnusedWindowHierarchies.MakeHooks());
            if (enableFlowRequestOptimizations.Value)
                hooks.AddRange(FlowRequests.MakeHooks());
            // BurstMath is handled in prepatcher.
            // MiscCleanups is handled in prepatcher.
            Application.quitting += BurstifyTransformFrames.DisposeCachedAllocations;
            if (Debug.isDebugBuild)
            {
                hooks.AddRange(AdditionalProfilerTags.MakeHooks());
            }

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
