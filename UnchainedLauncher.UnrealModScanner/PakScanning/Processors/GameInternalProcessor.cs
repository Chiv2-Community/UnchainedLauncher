using UnchainedLauncher.UnrealModScanner.Models;
using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.PakScanning.Processors {
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