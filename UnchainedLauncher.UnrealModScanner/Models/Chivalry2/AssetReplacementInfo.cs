using System.Text.Json.Serialization;
using UnchainedLauncher.UnrealModScanner.Assets;

namespace UnchainedLauncher.UnrealModScanner.Models.Chivalry2 {
    namespace UnchainedLauncher.UnrealModScanner.Models {
        [Obsolete("Use GenericAssetEntry in the future")]
        public sealed class AssetReplacementInfo : BaseAsset {
            /// <summary>
            /// File extension (.uasset, .umap, etc)
            /// </summary>
            [JsonPropertyName("extension")]
            public string Extension { get; init; } = string.Empty;
        }
    }

}