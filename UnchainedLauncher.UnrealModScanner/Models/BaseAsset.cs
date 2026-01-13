
using Newtonsoft.Json;

namespace UnchainedLauncher.UnrealModScanner.Models {
    public class BaseAsset {
        /// <summary>
        /// Full Unreal asset path including extension
        /// e.g. TBL/Content/Characters/Knight/Knight.uasset
        /// </summary>
        [JsonProperty("asset_path", Order = -2)]
        public string AssetPath { get; init; } = string.Empty;

        /// <summary>
        /// Hash of the pak entry (or file hash if available)
        /// </summary>
        [JsonProperty("asset_hash", Order = 100)]
        public string AssetHash { get; init; }
    }
}
