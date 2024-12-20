using System.IO;
using ThunderKit.Core.Data;
using UnityEditor;
using UnityEngine;
using System.IO.Compression;

namespace TurboMode.Editor
{
    public class BuildTurboMode : MonoBehaviour
    {
        [MenuItem("Tools/TurboMode/Build (debug) and run")]
        public static void BuildDebugAndRun()
        {
            if (Build(true))
            {
                var tkSettings = ThunderKitSettings.GetOrCreateSettings<ThunderKitSettings>();
                System.Diagnostics.Process.Start(Path.Combine(tkSettings.GamePath, tkSettings.GameExecutable));
            }
        }

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

        [MenuItem("Tools/TurboMode/Build Release Package")]
        public static void BuildReleasePackage()
        {
            Build(false, "package");
            var packageZip = $"{TurboModePlugin.pluginId}-{TurboModePlugin.pluginVersion}.zip";
            File.Delete(packageZip);
            ZipFile.CreateFromDirectory("package", packageZip, System.IO.Compression.CompressionLevel.Optimal, false);
            Directory.Delete("package", true);
        }

        private static bool Build(bool debug, string targetDir = null)
        {
            var tkSettings = ThunderKitSettings.GetOrCreateSettings<ThunderKitSettings>();

            if (string.IsNullOrEmpty(targetDir))
            {
                targetDir = tkSettings.GamePath;
            }

            // config
            string modName = "TurboMode";
            var tmPluginDir = Path.Combine(targetDir, "BepInEx", "plugins", modName);
            var bepInExPrepatchDir = Path.Combine(targetDir, "BepInEx", "patchers");
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

            Directory.CreateDirectory(tmPluginDir);
            Directory.CreateDirectory(bepInExPrepatchDir);

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

                // Copy Burst library
                var burstedSrc = Path.Combine(buildFolder, $"{modName}_Data/Plugins/x86_64/lib_burst_generated.dll");
                // If the file extension is .dll, SpaceWarp and BepInEx will log exceptions.
                var burstedDest = Path.Combine(tmPluginDir, TurboModePlugin.burstCodeAssemblyName);

                if (File.Exists(burstedSrc))
                {
                    CopyOverwrite(burstedSrc, burstedDest);

                    var burstPdbDest = burstedDest.Replace(".dll", ".pdb");
                    if (debug)
                    {
                        CopyOverwrite(burstedSrc.Replace(".dll", ".pdb"), burstPdbDest);
                    }
                    else
                    {
                        File.Delete(burstPdbDest);
                    }
                }

                // Copy BepInEx prepatch, which goes in a different directory.
                var prepatchSrc = Path.Combine(buildFolder, $"{modName}_Data/Managed/TurboMode.Prepatch.dll");
                var prepatchDest = Path.Combine(bepInExPrepatchDir, $"TurboMode.Prepatch.dll");
                CopyOverwrite(prepatchSrc, prepatchDest);

                // Copy swinfo.json
                CopyOverwrite("Assets/swinfo.json", Path.Combine(tmPluginDir, "swinfo.json"));

                Debug.Log("TurboMode build complete");
                return true;
            }
            else
            {
                Debug.LogError("TurboMode build failed");
                return false;
            }
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