
using Newtonsoft.Json;

namespace UnchainedLauncher.UnrealModScanner.Models {
    public sealed class ModMarkerInfo : BaseAsset {
        //[JsonProperty("asset_path")]
        //public required string AssetPath { get; init; }
        //[JsonProperty("asset_hash")]
        //public required string AssetHash { get; init; }

        //public required string Description { get; init; }

        [JsonProperty("blueprints")]
        public List<BlueprintModInfo> Blueprints { get; } = new();

        // TODO
        //public required GameMapInfo Map { get; init;  }
    }
}