using System.Text.Json.Serialization;

namespace UnchainedLauncher.Core.JsonModels {
    [JsonConverter(typeof(JsonStringEnumConverter))]
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