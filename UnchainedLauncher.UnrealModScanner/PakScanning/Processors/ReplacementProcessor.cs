using UnchainedLauncher.UnrealModScanner.Models.Chivalry2.UnchainedLauncher.UnrealModScanner.Models;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.PakScanning.Processors {
    public class ReplacementProcessor(IEnumerable<string> standardDirs) : IAssetProcessor {
        // FIXME: Use game name from config
        private readonly HashSet<string> _dirs = standardDirs.Select(d => $"TBL/Content/{d}".ToLower()).ToHashSet();

        public void Process(ScanContext ctx, PakScanResult result) {
            if (!_dirs.Any(dir => ctx.FilePath.ToLower().StartsWith(dir))) return;

            foreach (var export in ctx.Package.ExportsLazy) {
                result._AssetReplacements.Add(new AssetReplacementInfo {
                    AssetPath = ctx.FilePath,
                    // FIXME: add hash
                    ClassName = export.Value?.Class?.Name,
                    Extension = Path.GetExtension(ctx.FilePath)
                });
                break; // Found a valid export in a restricted dir
            }
        }
    }
}