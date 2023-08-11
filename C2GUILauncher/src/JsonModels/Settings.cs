using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace C2GUILauncher.JsonModels {
    record SavedSettings(
        [property: JsonProperty("installation_type")] InstallationType InstallationType,
        [property: JsonProperty("enable_plugin_logging")] bool EnablePluginLogging,
        [property: JsonProperty("enable_plugin_automatic_updates")] bool EnablePluginAutomaticUpdates
    );

    [JsonConverter(typeof(StringEnumConverter))]
    public enum InstallationType { NotSet, Steam, EpicGamesStore }

}
