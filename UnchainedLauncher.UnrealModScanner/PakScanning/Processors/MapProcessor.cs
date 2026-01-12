using CUE4Parse.UE4.Objects.UObject;
using UnchainedLauncher.UnrealModScanner.Models;
using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.PakScanning.Processors {
    public class MapProcessor : IAssetProcessor {
        public void Process(ScanContext ctx, PakScanResult result) {
            if (!ctx.FilePath.EndsWith(".umap", StringComparison.OrdinalIgnoreCase)) return;

            var worldSettingsExport = ctx.Package.ExportsLazy
            .FirstOrDefault(e => e.Value?.Class?.Name.Contains("WorldSettings", StringComparison.OrdinalIgnoreCase) == true)
            .Value;

            // 1. Handle the Null Error
            if (worldSettingsExport == null) {
                // Log that we found a map but couldn't find the settings
                System.Diagnostics.Debug.WriteLine($"[MapProcessor] No WorldSettings found in {ctx.FilePath}");
                return;
            }

            // 2. Safe Property Extraction
            // GetOrDefault is safer than manual FirstOrDefault logic
            var modeProp = worldSettingsExport.GetOrDefault<FPackageIndex>("DefaultGameMode");

            result._Maps.Add(new GameMapInfo {
                AssetPath = ctx.FilePath,
                AssetHash = ctx.GetHash(),
                GameMode = modeProp?.Name ?? "None"
            });
        }
    }
}