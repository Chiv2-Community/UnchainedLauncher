using UnchainedLauncher.UnrealModScanner.Config;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;

namespace UnchainedLauncher.PakScannerApp {
    public sealed record ScanConfiguration {
        public string? PakDirectory { get; init; }
        public string? OutputDirectory { get; init; }
        public ScanMode? ScanMode { get; init; }
        public string[]? OfficialDirectories { get; init; }
        public ScanOptions? Options { get; init; }
    }
}
