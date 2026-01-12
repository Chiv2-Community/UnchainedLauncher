
namespace UnchainedLauncher.UnrealModScanner.Models {
    public sealed record GameMapInfo {
        public required string AssetPath { get; init; }
        public required string AssetHash { get; init; }

        public string? GameMode { get; init; }
    }
}