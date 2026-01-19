using System.Collections.Concurrent;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.PakScanning.Processors.Obsolete;

/// <summary>
/// Processes a pak file and retrieves a list of assets.
/// This process is much slower than using AssetRegistry
/// <br/>
/// TODO: Convert this to use proper asset type or just remove 
/// </summary>
[Obsolete("Useless")]
public class GameInventoryProcessor : IAssetProcessor {
    /// <summary>
    /// List of Folders inside Game folder
    /// <br/>
    /// (e.g. TBL/Content/Charaters becomes Characters)
    /// </summary>
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

// FIXME: should this use GameInternalAssetInfo?
public record AssetEntry {
    public string Path { get; init; }
    public string ClassName { get; init; }
    public string ObjectName { get; init; }
}