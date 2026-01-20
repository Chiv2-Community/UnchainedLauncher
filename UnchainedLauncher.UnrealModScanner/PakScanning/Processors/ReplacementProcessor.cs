using UnchainedLauncher.UnrealModScanner.Assets;
using UnchainedLauncher.UnrealModScanner.AssetSources;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.PakScanning.Processors {
    public class ReplacementProcessor(IEnumerable<string> vanillaAssetDirs) : IAssetProcessor {
        public void Process(ScanContext ctx, PakScanResult result) {
            bool isVanilla = vanillaAssetDirs.Any(dir => ctx.FilePath.StartsWith(dir, StringComparison.InvariantCultureIgnoreCase));
            if (!isVanilla) return;

            var entry = GenericAssetEntry.FromSource(
                new ScanContextAssetSource(ctx),
                null
            );
            result._AssetReplacements.Add(entry);
        }
    }
}