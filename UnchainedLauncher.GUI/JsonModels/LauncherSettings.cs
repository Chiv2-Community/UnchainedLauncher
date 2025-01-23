using UnchainedLauncher.Core.JsonModels;
using System.Text.Json.Serialization;

namespace UnchainedLauncher.GUI.JsonModels {
    public record LauncherSettings(
        [property: JsonPropertyName("installation_type")] InstallationType? InstallationType,
        [property: JsonPropertyName("enable_plugin_automatic_updates")] bool? EnablePluginAutomaticUpdates,
        [property: JsonPropertyName("additional_mod_actors")] string? AdditionalModActors,
        [property: JsonPropertyName("server_browser_backend")] string? ServerBrowserBackend
    );
}