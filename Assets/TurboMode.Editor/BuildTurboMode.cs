using Codice.Client.Common.GameUI;
using System.IO;
using System.Reflection;
using ThunderKit.Core.Data;
using UnityEditor;
using UnityEngine;

namespace TurboMode.Editor
{

    public class BuildTurboMode : MonoBehaviour
    {
        [MenuItem("Tools/TurboMode/Build (debug)")]
        public static void BuildDebug()
        {
            Build(true);
        }

        [MenuItem("Tools/TurboMode/Build (release)")]
        public static void BuildRelease()
        {
            Build(false);
        }

        private static void Build(bool debug)
        {
            var tkSettings = ThunderKitSettings.GetOrCreateSettings<ThunderKitSettings>();

            // config
            string modName = "TurboMode";
            var tmPluginDir = Path.Combine(tkSettings.GamePath, "BepInEx", "plugins", modName);
            var buildFolder = "build";
            var pluginSrcManagedAssemblies = new string[]
            {
                "TurboMode",
                "Unity.Entities",
                "Unity.Mathematics.Extensions",
                "Unity.Profiling.Core",
                "Unity.Serialization",
            };

            BuildPlayerOptions buildPlayerOptions = new()
            {
                scenes = new[] { "Assets/test.unity" },
                locationPathName = $"{buildFolder}/{modName}.exe",
                target = BuildTarget.StandaloneWindows64,

                // use these options for the first build
                //buildPlayerOptions.options = BuildOptions.Development;

                // use these options for building scripts
                options = BuildOptions.BuildScriptsOnly | (debug ? BuildOptions.Development : 0)
            };

            var report = BuildPipeline.BuildPlayer(buildPlayerOptions);
            if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                // Copy Managed library
                foreach (var assmeblyName in pluginSrcManagedAssemblies)
                {
                    var managedSrc = Path.Combine(buildFolder, $"{modName}_Data/Managed/{assmeblyName}.dll");
                    var managedDest = Path.Combine(tmPluginDir, $"{assmeblyName}.dll");
                    CopyOverwrite(managedSrc, managedDest);

                    var managedPdbDest = managedDest.Replace(".dll", ".pdb");
                    if (debug)
                    {
                        var managedPdbSrc = managedSrc.Replace(".dll", ".pdb");
                        CopyOverwrite(managedPdbSrc, managedPdbDest);
                    }
                    else
                    {
                        File.Delete(managedPdbDest);
                    }
                }
            }

            // Copy Burst library
            var burstedSrc = Path.Combine(buildFolder, $"{modName}_Data/Plugins/x86_64/lib_burst_generated.dll");
            // If the file extension is .dll, SpaceWarp and BepInEx will log exceptions.
            var burstedDest = Path.Combine(tmPluginDir, TurboModePlugin.burstCodeAssemblyName);
            CopyOverwrite(burstedSrc, burstedDest);

            var burstPdbDest = burstedDest.Replace(".dll", ".pdb");
            if (debug)
            {
                CopyOverwrite(burstedSrc.Replace(".dll", ".pdb"), burstPdbDest);
            }
            else
            {
                File.Delete(burstedDest);
            }

            Debug.Log("TurboMode build complete");
        }

        private static void CopyOverwrite(string src, string dest)
        {
            FileUtil.DeleteFileOrDirectory(dest);
            if (!File.Exists(dest))
                FileUtil.CopyFileOrDirectory(src, dest);
            else
                Debug.LogWarning($"Couldn't update manged dll, {dest} is it currently in use?");
        }
    }
}