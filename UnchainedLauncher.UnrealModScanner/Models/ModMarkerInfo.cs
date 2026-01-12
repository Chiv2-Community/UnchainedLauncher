
namespace UnchainedLauncher.UnrealModScanner.Models {
    public sealed record ModMarkerInfo {
        public required string MarkerAssetPath { get; init; }
        public required string MarkerAssetHash { get; init; }

        //public required string Description { get; init; }

        public List<BlueprintModInfo> Blueprints { get; } = new();

        // TODO
        //public required GameMapInfo Map { get; init;  }
    }
}
