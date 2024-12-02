using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace UnchainedLauncher.Core.JsonModels {
    [JsonConverter(typeof(StringEnumConverter))]
    public enum InstallationType { NotSet, Steam, EpicGamesStore }

    public static class InstallationTypeExtensions {
        public static string ToFriendlyString(this InstallationType installationType) {
            return installationType switch {
                InstallationType.Steam => "Steam",
                InstallationType.EpicGamesStore => "Epic Games Store",
                _ => "Unknown"
            };
        }
    }
}
