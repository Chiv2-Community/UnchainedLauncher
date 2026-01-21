using UnchainedLauncher.UnrealModScanner.Assets;
using UnchainedLauncher.UnrealModScanner.AssetSources;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.PakScanning.Processors {
    public class ReplacementProcessor(IEnumerable<string> vanillaPakNames, IEnumerable<string> vanillaAssetDirs) : IAssetProcessor {
        private HashSet<string> VanillaPakNames { get; } = new HashSet<string>(vanillaPakNames, StringComparer.InvariantCultureIgnoreCase);
        private HashSet<string> VanillaAssetDirs { get; } = new HashSet<string>(vanillaAssetDirs, StringComparer.InvariantCultureIgnoreCase);
        
        public void Process(ScanContext ctx, PakScanResult result) {
            bool isInVanillaPak = VanillaPakNames.Contains(ctx.PakEntry.PakFileReader.Name);
            if (isInVanillaPak) return;
            
            bool isInVanillaPath = vanillaAssetDirs.Any(dir => ctx.FilePath.StartsWith(dir, StringComparison.InvariantCultureIgnoreCase));
            if (!isInVanillaPath) return;
            

            var entry = GenericAssetEntry.FromSource(
                new ScanContextAssetSource(ctx),
                null
            );
            result._AssetReplacements.Add(entry);
        }
    }
}