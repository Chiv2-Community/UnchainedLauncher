using UnchainedLauncher.UnrealModScanner.Assets;
using UnchainedLauncher.UnrealModScanner.AssetSources;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.PakScanning.Processors {
    public class ReplacementProcessor(IEnumerable<string> standardDirs) : IAssetProcessor {
        // FIXME: Use game name from config
        private readonly HashSet<string> _dirs = standardDirs.Select(d => $"TBL/Content/{d}").ToHashSet();

        public void Process(ScanContext ctx, PakScanResult result) {
            var replacement = (!_dirs.Any(dir => ctx.FilePath.StartsWith(dir)));
            if (!_dirs.Any(dir => ctx.FilePath.StartsWith(dir))) return;

            var entry = GenericAssetEntry.FromSource(
                new ScanContextAssetSource(ctx),
                null
            );
            result._AssetReplacements.Add(entry);
        }
    }
}