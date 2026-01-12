using System.Collections.Concurrent;
using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.PakScanning.Processors;

public class GameInventoryProcessor : IAssetProcessor {
    public ConcurrentBag<string> DefaultFolders { get; } = new();
    public ConcurrentBag<AssetEntry> AssetManifest { get; } = new();

    public void Process(ScanContext ctx, PakScanResult result) {
        // Log the folder path (e.g., TBL/Content/Characters -> Characters)
        var parts = ctx.FilePath.Split('/');
        if (parts.Length > 2) DefaultFolders.Add(parts[2]);

        // List assets and their types
        foreach (var export in ctx.Package.ExportsLazy) {
            if (export.Value == null) continue;

            AssetManifest.Add(new AssetEntry {
                Path = ctx.FilePath,
                ClassName = export.Value.Class?.Name ?? "Unknown",
                ObjectName = export.Value.Name
            });
            break; // Usually one main asset per file is enough for a manifest
        }
    }
}

public record AssetEntry {
    public string Path { get; init; }
    public string ClassName { get; init; }
    public string ObjectName { get; init; }
}
