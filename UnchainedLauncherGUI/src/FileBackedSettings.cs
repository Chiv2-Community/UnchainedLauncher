using Newtonsoft.Json;
using log4net;
using UnchainedLauncher.Core.Utilities;
using System.IO;

namespace UnchainedLauncher.GUI {
    public class FileBackedSettings<T> {
        public static readonly ILog logger = LogManager.GetLogger(nameof(FileBackedSettings<T>));
        public string SettingsFilePath { get; }

        public FileBackedSettings(string settingsFilePath) {
            SettingsFilePath = settingsFilePath;
        }

        public T? LoadSettings() {

            if (!File.Exists(SettingsFilePath))
                return default;

            var savedSettings = JsonHelpers.Deserialize<T>(File.ReadAllText(SettingsFilePath));

            if (savedSettings.Success) {
                return savedSettings.Result!;
            } else {
                var cause = savedSettings.Exception?.Message ?? "Schema does not match";
                logger.Error($"Settings malformed or from an unsupported old version.");

                if (cause != null)
                    logger.Error(cause);

                return default;
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
