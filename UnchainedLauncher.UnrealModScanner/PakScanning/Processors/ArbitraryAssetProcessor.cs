using UnchainedLauncher.UnrealModScanner.Assets;
using UnchainedLauncher.UnrealModScanner.AssetSources;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.PakScanning.Processors {
    /// <summary>
    /// Returns Assets from whitelisted directories which do not fall into other categories 
    /// and are not asset replacements
    /// </summary>
    public class ArbitraryAssetProcessor(IEnumerable<string> vanillaPakNames, IEnumerable<string> vanillaAssetDirs) : IAssetProcessor {
        private HashSet<string> VanillaPakNames { get; } = new HashSet<string>(vanillaPakNames, StringComparer.InvariantCultureIgnoreCase);
        private HashSet<string> VanillaAssetDirs { get; } = new HashSet<string>(vanillaAssetDirs, StringComparer.InvariantCultureIgnoreCase);
        
        public void Process(ScanContext ctx, PakScanResult result) {
            if (ctx.FilePath.EndsWith(".umap")) return;
            
            bool isInVanillaPak = VanillaPakNames.Contains(ctx.PakEntry.PakFileReader.Name);
            if (isInVanillaPak) return;
            
            bool isInVanillaPath = VanillaAssetDirs.Any(dir => ctx.FilePath.StartsWith(dir, StringComparison.InvariantCultureIgnoreCase));
            if (isInVanillaPath) return;

            // Debug.WriteLine($"Processing: {ctx.FilePath}");
            var entry = GenericAssetEntry.FromSource(
                new ScanContextAssetSource(ctx),
                null
            );
            result.ArbitraryAssets.Add(entry);
            return;
            // UObject? classExport = null;
            // try {
            //     var (mainExport, index) = BaseAsset.GetMainExport(ctx.Package).GetValueOrDefault();
            //     if (mainExport != null) {
            //         classExport = mainExport.ClassIndex.Load();
            //         if (mainExport.ClassName.EndsWith("BlueprintGeneratedClass"))
            //         {
            //             Debug.WriteLine("Using BlueprintGeneratedClass");
            //         }
            //         
            //         var entry = new ArbitraryAssetInfo {
            //             AssetPath = ctx.FilePath,
            //             AssetHash = HashUtility.GetAssetHash(ctx.Provider, ctx.FilePath, classExport),
            //             ClassName = classExport.Name, // ?? uClass.Name,
            //             // We extract ModName/Author even here, as some modders 
            //             // add metadata to custom classes without using a Marker
            //             // FIXME: This is deprecated
            //             // ModName = cdo?.GetOrDefault<string>("ModName") ?? "Unknown",
            //         };
            //         result.ArbitraryAssets.Add(entry);
            //     }
            //     else Debug.WriteLine($"Can't find mainExport for {ctx.Package.Name}");
            //
            //     return;
            // }
            // catch (Exception ex) {
            //     Debug.WriteLine(ex.Message);
            // }

            // Skip if it's a map or already identified as a replacement
            // if (ctx.FilePath.EndsWith(".umap")) return;
            //
            // // Logic: If it's NOT in a standard directory, it's an arbitrary custom asset
            // bool isStandard = _standardDirs.Any(dir => ctx.FilePath.ToLower().StartsWith(dir.ToLower()));
            //
            // if (!isStandard) {
            //     foreach (var export in ctx.Package.ExportsLazy) {
            //         try {
            //             if (export.Value is UClass uClass) {
            //                 var cdo = uClass.ClassDefaultObject.Load();
            //
            //                 result.ArbitraryAssets.Add(new ArbitraryAssetInfo {
            //                     AssetPath = ctx.FilePath,
            //                     AssetHash = HashUtility.GetAssetHash(ctx.Provider, ctx.FilePath, uClass),
            //                     ClassName = uClass.SuperStruct?.Name,// ?? uClass.Name,
            //                     // We extract ModName/Author even here, as some modders 
            //                     // add metadata to custom classes without using a Marker
            //                     // FIXME: This is deprecated
            //                     ModName = cdo?.GetOrDefault<string>("ModName") ?? "Unknown",
            //                 });
            //             }    
            //         }
            //         catch (Exception ex) {
            //             System.Diagnostics.Debug.WriteLine($"Processor Error: {ex.Message}");
            //             ctx.ProcessingErrors.Add(new AssetProcessingError {
            //                 ProcessorName = this.GetType().Name,
            //                 PackagePath = ctx.FilePath,
            //                 Message = ex.Message,
            //                 ExceptionType = ex.GetType().Name,
            //             });
            //         }
            //     }
            // }
        }
    }
}