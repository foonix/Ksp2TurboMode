using BepInEx;
using HarmonyLib;
using PA.ParticleField.Samples;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TurboMode
{
    [BepInPlugin("TurboMode", "TurboMode", "0.2.2.0")]
    public class TurboModePlugin : BaseUnityPlugin
    {
        private void Awake()
        {
            // Plugin startup logic
            Logger.LogInfo($"Plugin TurboMode is loaded!");
            var harmony = new Harmony("TurboMode");
            harmony.PatchAll(typeof(CollisionManagerPerformance));
            Logger.LogInfo($"TM: Adding profiler tags");
            harmony.PatchAll(typeof(AdditionalProfilerTags));

            // not sure how this gets enabled debug mode, but it's a huge amount of time for each message
            // Their logger api defaults to debug logging.
            KSP.Logging.GlobalLog.DisableFilter(KSP.Logging.LogFilter.Debug);
        }
    }
}
