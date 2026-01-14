using CUE4Parse.UE4.Objects.UObject;
using UnchainedLauncher.UnrealModScanner.Models;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.PakScanning.Processors {
    /// <summary>
    /// Parses .umap assets. Retrieves Game Mode name from WorldSettings
    /// </summary>
    public class MapProcessor : IAssetProcessor {
        public void Process(ScanContext ctx, PakScanResult result) {
            if (!ctx.FilePath.EndsWith(".umap", StringComparison.OrdinalIgnoreCase)) return;

            var worldSettingsExport = ctx.Package.ExportsLazy
            .FirstOrDefault(e => 
                e.Value.Class?.Name
                    .Contains("WorldSettings", StringComparison.OrdinalIgnoreCase) == true)
            ?.Value;

            if (worldSettingsExport == null) {
                System.Diagnostics.Debug.WriteLine($"[MapProcessor] No WorldSettings found in {ctx.FilePath}");
                return;
            }

            var modeProp = worldSettingsExport.GetOrDefault<FPackageIndex>("DefaultGameMode");
            
            result._Maps.Add(new GameMapInfo {
                AssetPath = ctx.FilePath,
                AssetHash = ctx.GetHash(),
                GameMode = modeProp?.Name ?? "None"
            });
        }
    }
}