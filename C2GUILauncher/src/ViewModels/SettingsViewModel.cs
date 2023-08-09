using C2GUILauncher.JsonModels;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace C2GUILauncher.ViewModels
{
    [AddINotifyPropertyChangedInterface]
    public class SettingsViewModel
    {

        private static readonly string SettingsFilePath = $"{FilePaths.ModCachePath}\\unchained_launcher_settings.json";

        public InstallationType InstallationType { get; set; }
        public bool EnablePluginLogging { get; set; }
        public bool EnablePluginAutomaticUpdates { get; set; }
        public string CLIArgs { get; set; }

        public static IEnumerable<InstallationType> AllInstallationTypes
        {
            get { return Enum.GetValues(typeof(InstallationType)).Cast<InstallationType>(); }
        }

        public SettingsViewModel(InstallationType installationType, bool enablePluginLogging, bool enablePluginAutomaticUpdates, string cliArgs)
        {
            InstallationType = installationType;
            EnablePluginLogging = enablePluginLogging;
            EnablePluginAutomaticUpdates = enablePluginAutomaticUpdates;
            CLIArgs = cliArgs;
        }

        public static SettingsViewModel LoadSettings()
        {
            var cliArgsList = Environment.GetCommandLineArgs();
            var cliArgs = cliArgsList.Length > 1 ? Environment.GetCommandLineArgs().Skip(1).Aggregate((x, y) => x + " " + y) : "";

            var defaultSettings = new SettingsViewModel(InstallationType.NotSet, false, true, cliArgs);

            if (!File.Exists(SettingsFilePath))
                return defaultSettings;

            var savedSettings = JsonConvert.DeserializeObject<SavedSettings>(File.ReadAllText(SettingsFilePath));

            if (savedSettings is not null)
            {
                return new SettingsViewModel(savedSettings.InstallationType, savedSettings.EnablePluginLogging, savedSettings.EnablePluginAutomaticUpdates, cliArgs);
            }
            else
            {
                MessageBox.Show("Settings malformed or from an unsupported old version. Loading defaults.");
                return defaultSettings;
            }
        }

        public void SaveSettings()
        {
            var settings = new SavedSettings(InstallationType, EnablePluginLogging, EnablePluginAutomaticUpdates);
            var json = JsonConvert.SerializeObject(settings, Newtonsoft.Json.Formatting.Indented);

            if(!Directory.Exists(FilePaths.ModCachePath))
                Directory.CreateDirectory(FilePaths.ModCachePath);

            File.WriteAllText(SettingsFilePath, json);
        }

    }
}
