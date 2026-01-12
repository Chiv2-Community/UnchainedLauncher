
namespace UnchainedLauncher.UnrealModScanner.Models {
    namespace UnchainedLauncher.UnrealModScanner.Models {
        public sealed class AssetReplacementInfo {
            /// <summary>
            /// Full Unreal asset path including extension
            /// e.g. TBL/Content/Characters/Knight/Knight.uasset
            /// </summary>
            public string AssetPath { get; init; } = string.Empty;

            /// <summary>
            /// Name of the pak this asset was found in
            /// </summary>
            public string PakName { get; init; } = string.Empty;

            /// <summary>
            /// Hash of the pak entry (or file hash if available)
            /// </summary>
            public string AssetHash { get; init; }

            /// <summary>
            /// File extension (.uasset, .umap, etc)
            /// </summary>
            public string Extension { get; init; } = string.Empty;

            /// <summary>
            /// Optional: UE class name if resolvable (can be null)
            /// </summary>
            public string? ClassType { get; init; }

            /// <summary>
            /// True if this asset is under a “standard” game directory
            /// and therefore overrides base game content
            /// </summary>
            public bool IsReplacement { get; init; } = true;
        }
    }

}
