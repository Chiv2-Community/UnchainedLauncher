﻿using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace C2GUILauncher.JsonModels {
    public record LauncherSettings(
        [property: JsonProperty("installation_type")] InstallationType? InstallationType,
        [property: JsonProperty("enable_plugin_automatic_updates")] bool? EnablePluginAutomaticUpdates,
        [property: JsonProperty("additional_mod_actors")] string? AdditionalModActors,
        [property: JsonProperty("server_browser_backend")] string? ServerBrowserBackend
    );

    [JsonConverter(typeof(StringEnumConverter))]
    public enum InstallationType { NotSet, Steam, EpicGamesStore }

}
