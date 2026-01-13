
using Newtonsoft.Json;

namespace UnchainedLauncher.UnrealModScanner.Models {
    public sealed class ArbitraryAssetInfo : BaseAsset {

        //[JsonProperty("asset_path")]
        //public string AssetPath { get; init; } = string.Empty;

        //[JsonProperty("asset_hash")]
        //public string AssetHash { get; init; } = string.Empty;

        [JsonProperty("object_class")]
        public string ObjectName { get; init; } = string.Empty;

        [JsonProperty("mod_name")]
        public string ModName { get; init; }
    }
}