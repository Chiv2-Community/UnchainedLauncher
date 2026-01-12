
namespace UnchainedLauncher.UnrealModScanner.Models {
    public sealed class ArbitraryAssetInfo {

        public string AssetPath { get; init; } = string.Empty;

        public string ObjectName { get; init; } = string.Empty;

        public string ModName { get; init; }

        public string AssetHash { get; init; } = string.Empty;
    }
}
