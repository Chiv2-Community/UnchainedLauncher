
using CUE4Parse.UE4.AssetRegistry.Objects;
using Newtonsoft.Json;

namespace UnchainedLauncher.UnrealModScanner.Models {
    public class GameInternalAssetInfo : BaseAsset {
        //[JsonProperty("asset_path")]
        //public string AssetPath { get; set; }
        [JsonProperty("class_name")]
        public string ClassName { get; set; }
        [JsonProperty("package_name")]
        public string FullPackageName { get; set; }
        [JsonProperty("asset_data")]
        public FAssetData AssetData { get; set; }
    }
}