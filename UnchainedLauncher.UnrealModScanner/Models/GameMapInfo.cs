
using Newtonsoft.Json;

namespace UnchainedLauncher.UnrealModScanner.Models {
    public sealed class GameMapInfo : BaseAsset {
        //[JsonProperty("asset_path")]
        //public required string AssetPath { get; init; }
        //[JsonProperty("asset_hash")]
        //public required string AssetHash { get; init; }
        [JsonProperty("game_mode")]
        public string? GameMode { get; init; }
    }
}