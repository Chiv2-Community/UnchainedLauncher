using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace UnchainedLauncherCore.JsonModels {
    [JsonConverter(typeof(StringEnumConverter))]
    public enum InstallationType { NotSet, Steam, EpicGamesStore }
}
