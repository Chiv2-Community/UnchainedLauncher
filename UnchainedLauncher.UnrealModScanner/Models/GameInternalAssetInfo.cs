
using CUE4Parse.UE4.AssetRegistry.Objects;
using Newtonsoft.Json;
using UnchainedLauncher.UnrealModScanner.Models.Dto;

namespace UnchainedLauncher.UnrealModScanner.Models {
    /// <summary>
    /// Holds information about an asset retrieved via AssetRegistry.bin scan
    /// </summary>
    public class GameInternalAssetInfo : BaseAsset {
        /// <summary>
        /// Asset's Package name. Also in the AssetData
        /// </summary>
        [JsonProperty("package_name")]
        public string FullPackageName { get; set; }
        /// <summary>
        /// FAssetData from AssetRegistry.bin
        /// </summary>
        [JsonProperty("asset_data")]
        public FAssetData AssetData { get; set; }
    }
}