using DiscriminatedUnions;
using log4net;
using System.IO;
using System.Text.Json;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.GUI {
    public class FileBackedSettings<T> {
        public static readonly ILog logger = LogManager.GetLogger(nameof(FileBackedSettings<T>));
        public string SettingsFilePath { get; }

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions {
            Converters = { new UnionConverterFactory() },
            WriteIndented = true
        };

        public FileBackedSettings(string settingsFilePath) {
            SettingsFilePath = settingsFilePath;
        }

        public T? LoadSettings() {
            if (!File.Exists(SettingsFilePath))
                return default;

            var savedSettings = JsonHelpers.Deserialize<T>(File.ReadAllText(SettingsFilePath));

            if (savedSettings.Success) {
                return savedSettings.Result!;
            }
            else {
                var cause = savedSettings.Exception?.Message ?? "Schema does not match";
                logger.Error($"Settings malformed or from an unsupported old version.");

                if (cause != null)
                    logger.Error(cause);

                return default;
            }
        }

        public void SaveSettings(T settings) {
            var json = JsonSerializer.Serialize(settings, _jsonOptions);

            var settingsDir = Path.GetDirectoryName(SettingsFilePath) ?? "";
            Directory.CreateDirectory(settingsDir);

            File.WriteAllText(SettingsFilePath, json);
        }
    }
}