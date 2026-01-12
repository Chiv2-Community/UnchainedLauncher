using UnchainedLauncher.UnrealModScanner.Models;
//using UnrealModScanner.Models;

namespace UnrealModScanner.Export;

public sealed record ModScanDocument {
    public string SchemaVersion { get; init; } = "1.0";
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
    //public required IReadOnlyList<PakScanResult> Paks { get; init; }
    public required TechnicalManifest Manifest { get; init; }
}
