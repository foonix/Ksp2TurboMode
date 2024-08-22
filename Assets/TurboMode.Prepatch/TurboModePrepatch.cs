using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Mono.Cecil;
using System.IO;

namespace TurboMode.Prepatch
{
    public abstract class TurboModePrepatch
    {
        protected static ManualLogSource logSource;
        protected static ConfigFile config;
        protected static AssemblyDefinition tmAssembly;
        private static bool isInitialized;

        protected static void InitSharedResources()
        {
            if (isInitialized) return;

            logSource = Logger.CreateLogSource("TurboMode.Preload");
            logSource.LogInfo("BurstMath Initialize()");

            var configPath = Utility.CombinePaths(Paths.ConfigPath, "TurboMode.cfg");
            config = new ConfigFile(configPath, saveOnInit: false)
            {
                // We have to Bind() here to get the value, but avoid writing because we don't have all of the settings.
                SaveOnConfigSet = false
            };

            // Get our main assembly signatures without actually loading it.
            var thisAsmPath = typeof(BurstMathInjector).Assembly.Location;
            tmAssembly = AssemblyDefinition.ReadAssembly(Path.Combine(Path.GetDirectoryName(thisAsmPath), "..", "plugins", "TurboMode", "TurboMode.dll"));


            isInitialized = true;
        }

        protected static void CleanupSharedResources()
        {
            if (!isInitialized) return;

            // free up unused
            config = null;
            logSource = null;
            tmAssembly = null;

            isInitialized = false;
        }
    }
}
