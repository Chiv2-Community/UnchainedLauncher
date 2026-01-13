
using Newtonsoft.Json;

namespace UnchainedLauncher.UnrealModScanner.Models {
    namespace UnchainedLauncher.UnrealModScanner.Models {
        public sealed class AssetReplacementInfo : BaseAsset {
            ///// <summary>
            ///// Full Unreal asset path including extension
            ///// e.g. TBL/Content/Characters/Knight/Knight.uasset
            ///// </summary>
            //[JsonProperty("asset_path")]
            //public string AssetPath { get; init; } = string.Empty;

            ///// <summary>
            ///// Hash of the pak entry (or file hash if available)
            ///// </summary>
            //[JsonProperty("asset_hash")]
            //public string AssetHash { get; init; }

            /// <summary>
            /// Name of the pak this asset was found in
            /// </summary>
            [JsonIgnore]
            public string PakName { get; init; } = string.Empty;

            /// <summary>
            /// File extension (.uasset, .umap, etc)
            /// </summary>
            [JsonProperty("extension")]
            public string Extension { get; init; } = string.Empty;

            /// <summary>
            /// Optional: UE class name if resolvable (can be null)
            /// </summary>
            [JsonProperty("class_typ")]
            public string? ClassType { get; init; }

            /// <summary>
            /// True if this asset is under a “standard” game directory
            /// and therefore overrides base game content
            /// </summary>
            [JsonIgnore]
            public bool IsReplacement { get; init; } = true;
        }
    }

}