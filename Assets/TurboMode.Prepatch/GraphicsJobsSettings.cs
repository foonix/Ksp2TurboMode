using Mono.Cecil;
using System.Collections.Generic;

namespace TurboMode.Prepatch
{

    public class GraphicsJobsSettings : TurboModePrepatch
    {
        public static IEnumerable<string> TargetDLLs { get; private set; } = new[] { "Assembly-CSharp.dll" };

        private static bool enabled;

        public static void Initialize()
        {
            InitSharedResources();

            // Can't get this from TurboModePlugin, because we must avoid loading Assembly-CSharp.dll.
            enabled = config.Bind(
                "General",
                "EnableGraphicsJobs",
                true,
                "Enable Unity Graphics jobs settings in boot.config.  Speeds up camera rendering. Note: This setting requires TWO restarts to take effect or change."
            ).Value;

            if (enabled)
            {
                logSource.LogInfo("Enabling options for unity graphics jobs in boot.config");
            }
            else
            {
                logSource.LogInfo("EnableGraphicsJobs option is disabled.  Enforcing the options to be off in boot.confg.");
            }
        }

        public static void Finish()
        {
            CleanupSharedResources();
        }

        public static void Patch(ref AssemblyDefinition _)
        {
            var bootConfig = new BootConfigEditor();

            var value = enabled ? "1" : "0";
            ChangeSettingAndLog(bootConfig, "gfx-enable-gfx-jobs", value);
            ChangeSettingAndLog(bootConfig, "gfx-enable-native-gfx-jobs", value);
            bootConfig.Save();
        }

        private static void ChangeSettingAndLog(BootConfigEditor editor, string setting, string value)
        {
            var currentValue = editor.GetSetting(setting);
            logSource.LogInfo($"Changing boot.config setting for {setting} from {currentValue} to {value}");
            editor.ChangeSetting(setting, value);
        }
    }
}