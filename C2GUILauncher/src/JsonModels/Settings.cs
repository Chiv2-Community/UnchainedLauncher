using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace C2GUILauncher.JsonModels {

    /// <summary>
    /// Saved settings are used to store the user's preferences for the launcher.
    /// </summary>
    /// <param name="InstallationType">An indicator for if this installation is steam, egs, or not set</param>
    /// <param name="EnablePluginLogging">Enables the debug version of any C2 Unchained core DLLs that get injected.  This enables some logging.</param>
    /// <param name="EnablePluginAutomaticUpdates">Automatically updates C2 Unchained core DLLs to their latest versions every launch.</param>
    record SavedSettings(
        [property: JsonProperty("installation_type")] InstallationType InstallationType,
        [property: JsonProperty("enable_plugin_logging")] bool EnablePluginLogging,
        [property: JsonProperty("enable_plugin_automatic_updates")] bool EnablePluginAutomaticUpdates
    );

    /// <summary>
    /// An indicator for if this installation is steam, egs, or not set
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum InstallationType { NotSet, Steam, EpicGamesStore }

}
