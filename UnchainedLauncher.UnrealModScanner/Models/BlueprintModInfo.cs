

namespace UnchainedLauncher.UnrealModScanner.Models {
    public sealed record BlueprintModInfo {
        public required string BlueprintPath { get; init; }
        public required string BlueprintHash { get; init; }

        public required string ModName { get; init; }
        public required string Version { get; init; }
        public required string Author { get; init; }
        public required bool IsClientSide { get; init; }
    }
}