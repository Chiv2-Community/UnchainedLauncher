using Newtonsoft.Json;

namespace UnchainedLauncher.GUI.JsonModels {
    public record ServerSettings(
        [property: JsonProperty("server_name")] string? ServerName,
        [property: JsonProperty("server_description")] string? ServerDescription,
        [property: JsonProperty("server_password")] string? ServerPassword,
        [property: JsonProperty("selected_map")] string? SelectedMap,
        [property: JsonProperty("game_port")] int? GamePort,
        [property: JsonProperty("rcon_port")] int? RconPort,
        [property: JsonProperty("a2s_port")] int? A2sPort,
        [property: JsonProperty("ping_port")] int? PingPort,
        [property: JsonProperty("show_in_server_browser")] bool? ShowInServerBrowser
    );
}