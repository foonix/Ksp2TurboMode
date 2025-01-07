using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TurboMode
{
    class BootConfigEditor
    {
        private readonly List<string> bootConfigLines;
        
        private const string bootConfigPath = "KSP2_x64_Data/boot.config";
        private const string bootConfigBackupPath = "KSP2_x64_Data/TurboModeBackup.boot.config";

        private readonly Regex optionParser = new($"^\\s*([^\\s]+)\\s*=\\s*(.+)\\s*$", RegexOptions.Compiled);

        /// <summary>
        /// Open boot.config file, taking a one-time backup first.
        /// </summary>
        public BootConfigEditor()
        {
            if (!File.Exists(bootConfigBackupPath))
            {
                File.Copy(bootConfigPath, bootConfigBackupPath);
            }

            bootConfigLines = File.ReadAllLines(bootConfigPath).ToList();
        }

        /// <summary>
        /// Write current setting in this object to the file.
        /// </summary>
        public void Save()
        {
            File.WriteAllLines(bootConfigPath, bootConfigLines);
        }

        /// <summary>
        /// Get boot.config current setting value
        /// </summary>
        public string GetSetting(string setting)
        {
            foreach (var line in bootConfigLines)
            {
                var match = optionParser.Match(line);
                if (match.Success && match.Groups[1].Value == setting)
                {
                    return match.Groups[2].Value;
                }
            }
            return "notfound";
        }

        /// <summary>
        /// Change or append boot.config setting
        /// </summary>
        public void ChangeSetting(string setting, string value)
        {
            var settingLine = $"{setting}={value}";
            for (int i = 0; i < bootConfigLines.Count; i++)
            {
                var match = optionParser.Match(bootConfigLines[i]);
                if (match.Success && match.Groups[1].Value == setting)
                {
                    bootConfigLines[i] = settingLine;
                    return;
                }
            }
            bootConfigLines.Add(settingLine);
        }
    }
}
