using CUE4Parse.FileProvider;
using UnchainedLauncher.UnrealModScanner.Models;
using UnchainedLauncher.UnrealModScanner.Models.UnchainedLauncher.UnrealModScanner.Models;
using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.Scanning {
    public sealed class ModScanContext {
        public IFileProvider Provider { get; }

        public Dictionary<string, List<BlueprintModInfo>> ModsByPak { get; }
        public Dictionary<string, List<AssetReplacementInfo>> ReplacementsByPak { get; }
        public Dictionary<string, PakScanResult> Merged { get; set; }

        public ModScanContext(
            IFileProvider provider,
            Dictionary<string, List<BlueprintModInfo>> modsByPak,
            Dictionary<string, List<AssetReplacementInfo>> replacementsByPak) {
            Provider = provider;
            ModsByPak = modsByPak;
            ReplacementsByPak = replacementsByPak;
        }
        
        public ModScanContext(
            IFileProvider provider,
            Dictionary<string, PakScanResult> merged) {
            Provider = provider;
            ModsByPak = new();
            ReplacementsByPak = new();
            Merged = merged;
        }
    }
}
