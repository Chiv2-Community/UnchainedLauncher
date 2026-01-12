

using CUE4Parse.UE4.Objects.UObject;
using UnchainedLauncher.UnrealModScanner.Models;
using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.PakScanning.Processors {
    public class ArbitraryBlueprintProcessor : IAssetProcessor {
        private readonly HashSet<string> _standardDirs;

        public ArbitraryBlueprintProcessor(IEnumerable<string> standardDirs) {
            _standardDirs = standardDirs.Select(d => $"TBL/Content/{d}".ToLower()).ToHashSet();
        }

        public void Process(ScanContext ctx, PakScanResult result) {
            // Skip if it's a map or already identified as a replacement
            if (ctx.FilePath.EndsWith(".umap")) return;

            // Logic: If it's NOT in a standard directory, it's an arbitrary custom asset
            bool isStandard = _standardDirs.Any(dir => ctx.FilePath.ToLower().StartsWith(dir.ToLower()));

            if (!isStandard) {
                foreach (var export in ctx.Package.ExportsLazy) {
                    if (export.Value is UClass uClass) {
                        var cdo = uClass.ClassDefaultObject.Load();

                        result.ArbitraryAssets.Add(new ArbitraryAssetInfo {
                            AssetPath = ctx.FilePath,
                            ObjectName = uClass.Name,
                            // We extract ModName/Author even here, as some modders 
                            // add metadata to custom classes without using a Marker
                            ModName = cdo?.GetOrDefault<string>("ModName"),
                            AssetHash = HashUtility.GetAssetHash(ctx.Provider, ctx.FilePath, uClass)
                        });
                    }
                }
            }
        }
    }
}
