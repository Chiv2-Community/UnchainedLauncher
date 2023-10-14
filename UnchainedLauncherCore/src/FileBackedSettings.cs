using Newtonsoft.Json;
using log4net;
using UnchainedLauncherCore.Utilities;

namespace UnchainedLauncherCore
{
    public class FileBackedSettings<T> {
        public static readonly ILog logger = LogManager.GetLogger(nameof(FileBackedSettings<T>));
        public string SettingsFilePath { get; }
        public T DefaultSettings { get; }

        public FileBackedSettings(string settingsFilePath, T defaultSettings) {
            SettingsFilePath = settingsFilePath;
            DefaultSettings = defaultSettings;
        }

        public T LoadSettings() {

            if (!File.Exists(SettingsFilePath))
                return DefaultSettings;

            var savedSettings = JsonHelpers.Deserialize<T>(File.ReadAllText(SettingsFilePath));

            if (savedSettings.Success) {
                return savedSettings.Result!;
            } else {
                var cause = savedSettings.Exception?.Message ?? "Schema does not match";
                logger.Error($"Settings malformed or from an unsupported old version. Loading defaults.");

                if (cause != null)
                    logger.Error(cause);

                return DefaultSettings;
            }
        }

        public void SaveSettings(T settings) {
            var json = JsonConvert.SerializeObject(settings, Formatting.Indented);

            var settingsDir = Path.GetDirectoryName(SettingsFilePath) ?? "";
            Directory.CreateDirectory(settingsDir);

            File.WriteAllText(SettingsFilePath, json);
        }
    }
}
