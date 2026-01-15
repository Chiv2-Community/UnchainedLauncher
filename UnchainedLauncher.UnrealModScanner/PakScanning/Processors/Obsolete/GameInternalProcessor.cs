using UnchainedLauncher.UnrealModScanner.Assets;
using UnchainedLauncher.UnrealModScanner.Models;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.PakScanning.Processors.Obsolete {
    /// <summary>
    /// Slow asset scan, only retrieving basic information
    /// </summary>
    [Obsolete("Useless")]
    public class GameInternalProcessor : IAssetProcessor {
        public void Process(ScanContext ctx, PakScanResult result) {
            // We only take the first export to identify the asset type
            var mainExport = ctx.Package.ExportsLazy.FirstOrDefault().Value;

            result.InternalAssets.Add(new GameInternalAssetInfo {
                AssetPath = ctx.FilePath,
                ClassName = mainExport?.Class?.Name ?? "Unknown",
                FullPackageName = ctx.Package.Name
            });
        }
    }
}