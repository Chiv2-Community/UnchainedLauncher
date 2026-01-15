using System.Collections.ObjectModel;
using UnchainedLauncher.UnrealModScanner.JsonModels;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnrealModScanner.Models;
//using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.Export;

public sealed record ModScanDocument {
    public string SchemaVersion { get; init; } = "1.0";
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
    //public required IReadOnlyList<PakScanResult> Paks { get; init; }
    public ModManifest Manifest { get; init; } = null;
    public ObservableCollection<PakScanResult> Results { get; init; } = null;
    public ModScanResult ScanResults { get; init; } = null;
}