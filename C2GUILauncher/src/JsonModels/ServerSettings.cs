using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace C2GUILauncher.JsonModels {
    public record ServerSettings(
        [property: JsonProperty("server_name")] string ServerName,
        [property: JsonProperty("server_description")] string ServerDescription,
        [property: JsonProperty("server_list")] string ServerList,
        [property: JsonProperty("game_port")] int GamePort,
        [property: JsonProperty("rcon_port")] int RconPort,
        [property: JsonProperty("a2s_port")] int A2sPort,
        [property: JsonProperty("ping_port")] int PingPort
    );
}
