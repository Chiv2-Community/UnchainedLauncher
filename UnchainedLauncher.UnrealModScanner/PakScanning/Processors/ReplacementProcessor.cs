using UnchainedLauncher.UnrealModScanner.Assets;
using UnchainedLauncher.UnrealModScanner.AssetSources;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.PakScanning.Processors {
    public class ReplacementProcessor(IEnumerable<string> vanillaAssetDirs) : IAssetProcessor {
        // FIXME: Use game name from config
        private readonly HashSet<string> _dirs = vanillaAssetDirs.ToHashSet();

        public void Process(ScanContext ctx, PakScanResult result) {
            if (!_dirs.Any(dir => ctx.FilePath.StartsWith(dir))) return;

            var entry = GenericAssetEntry.FromSource(
                new ScanContextAssetSource(ctx),
                null
            );
            result._AssetReplacements.Add(entry);
        }
    }
}