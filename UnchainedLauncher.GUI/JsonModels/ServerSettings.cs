using System.Text.Json.Serialization;
namespace UnchainedLauncher.GUI.JsonModels {
    public record ServerSettings(
        [property: JsonPropertyName("server_name")] string? ServerName,
        [property: JsonPropertyName("server_description")] string? ServerDescription,
        [property: JsonPropertyName("server_password")] string? ServerPassword,
        [property: JsonPropertyName("selected_map")] string? SelectedMap,
        [property: JsonPropertyName("game_port")] int? GamePort,
        [property: JsonPropertyName("rcon_port")] int? RconPort,
        [property: JsonPropertyName("a2s_port")] int? A2SPort,
        [property: JsonPropertyName("ping_port")] int? PingPort,
        [property: JsonPropertyName("show_in_server_browser")] bool? ShowInServerBrowser
    );
}