using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace UnchainedLauncher.Core.JsonModels {
    [JsonConverter(typeof(StringEnumConverter))]
    public enum InstallationType { NotSet, Steam, EpicGamesStore }
}
