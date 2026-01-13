

using Newtonsoft.Json;

namespace UnchainedLauncher.UnrealModScanner.Models {
    public sealed class BlueprintModInfo : BaseAsset {
        //[JsonProperty("asset_path")]
        //public required string AssetPath { get; init; }
        //[JsonProperty("asset_hash")]
        //public required string AssetHash { get; init; }
        [JsonProperty("mod_name")]
        public required string ModName { get; init; }
        [JsonProperty("mod_version")]
        public required string Version { get; init; }
        [JsonProperty("mod_author")]
        public required string Author { get; init; }
        [JsonProperty("is_clientside")]
        public required bool IsClientSide { get; init; }
    }
}