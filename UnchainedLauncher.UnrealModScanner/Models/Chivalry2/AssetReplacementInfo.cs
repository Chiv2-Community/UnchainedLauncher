using Newtonsoft.Json;
using UnchainedLauncher.UnrealModScanner.Assets;

namespace UnchainedLauncher.UnrealModScanner.Models.Chivalry2 {
    namespace UnchainedLauncher.UnrealModScanner.Models {
        [Obsolete("Use GenericAssetEntry in the future")]
        public sealed class AssetReplacementInfo : BaseAsset {
            /// <summary>
            /// File extension (.uasset, .umap, etc)
            /// </summary>
            [JsonProperty("extension")]
            public string Extension { get; init; } = string.Empty;
        }
    }

}