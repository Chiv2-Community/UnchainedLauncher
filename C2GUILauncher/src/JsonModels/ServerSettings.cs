using Newtonsoft.Json;
using System;
using System.Collections.ObjectModel;

namespace C2GUILauncher.JsonModels {
    public record ServerSettings(
        [property: JsonProperty("server_name")] string? ServerName,
        [property: JsonProperty("server_description")] string? ServerDescription,
        [property: JsonProperty("server_list")] string? ServerList,
        [property: JsonProperty("selected_map")] string? SelectedMap,
        [property: JsonProperty("game_port")] short? GamePort,
        [property: JsonProperty("rcon_port")] short? RconPort,
        [property: JsonProperty("a2s_port")] short? A2sPort,
        [property: JsonProperty("ping_port")] short? PingPort,
        [property: JsonProperty("show_in_server_browser")] bool? ShowInServerBrowser,
        [property: JsonProperty("selected_bool_options")] ObservableCollection<string>? SelectedBoolOptionsList,
        [property: JsonProperty("selected_mods")] ObservableCollection<string>? SelectedModsList,
        [property: JsonProperty("num_bots")] short? NumBots,
        [property: JsonProperty("max_fps")] short? MaxFPS,
        [property: JsonProperty("warmup_time")] double? WarmupTime,
        [property: JsonProperty("ffa_time_limit")] uint? FFATimeLimit,
        [property: JsonProperty("ffa_score_limit")] uint? FFAScoreLimit,
        [property: JsonProperty("tdm_time_limit")] uint? TDMTimeLimit,
        [property: JsonProperty("tbm_ticket_count")] uint? TDMTicketCount,
        [property: JsonProperty("ffa_time_limit_enable")] bool? EnableFFATimeLimit,
        [property: JsonProperty("ffa_score_limit_enable")] bool? EnableFFAScoreLimit,
        [property: JsonProperty("tdm_time_limit_enable")] bool? EnableTDMTimeLimit,
        [property: JsonProperty("tbm_ticket_count_enable")] bool? EnableTDMTicketCount,
        [property: JsonProperty("ini_override_enable")] bool? EnableIniOverrides,
        [property: JsonProperty("mods_enable")] bool? EnableMods
    );
}
