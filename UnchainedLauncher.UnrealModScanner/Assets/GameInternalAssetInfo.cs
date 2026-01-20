using CUE4Parse.UE4.AssetRegistry.Objects;
using System.Text.Json.Serialization;

namespace UnchainedLauncher.UnrealModScanner.Assets {
    /// <summary>
    /// Holds information about an asset retrieved via AssetRegistry.bin scan
    /// </summary>
    public class GameInternalAssetInfo : BaseAsset {
        /// <summary>
        /// Asset's Package name. Also in the AssetData
        /// </summary>
        [JsonPropertyName("package_name")]
        public string FullPackageName { get; set; }
        /// <summary>
        /// FAssetData from AssetRegistry.bin
        /// </summary>
        [JsonPropertyName("asset_data")]
        public FAssetData AssetData { get; set; }
    }
}