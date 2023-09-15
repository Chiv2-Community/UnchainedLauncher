using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace C2GUILauncher
{
    public class FileBackedSettings<T>
    {
        public string SettingsFilePath { get; }
        public T DefaultSettings { get; }

        public FileBackedSettings(string settingsFilePath, T defaultSettings) {
            SettingsFilePath = settingsFilePath;
            DefaultSettings = defaultSettings;
        }

        public T LoadSettings()
        {

            if (!File.Exists(SettingsFilePath))
                return DefaultSettings;

            var savedSettings = JsonHelpers.Deserialize<T>(File.ReadAllText(SettingsFilePath));

            if (savedSettings.Success)
            {
                return savedSettings.Result!;
            }
            else
            {
                var cause = savedSettings.Exception?.Message ?? "Schema does not match";
                MessageBox.Show($"Settings malformed or from an unsupported old version. Loading defaults. Cause: {cause}");
                return DefaultSettings;
            }
        }

        public void SaveSettings(T settings)
        {
            var json = JsonConvert.SerializeObject(settings, Formatting.Indented);

            var settingsDir = Path.GetDirectoryName(SettingsFilePath) ?? "";
            Directory.CreateDirectory(settingsDir);

            File.WriteAllText(SettingsFilePath, json);
        }
    }
}
