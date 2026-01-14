using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.UObject;
using System.Collections.Concurrent;
using UnchainedLauncher.UnrealModScanner.Models.Dto;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnchainedLauncher.UnrealModScanner.Utility;
using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.PakScanning.Processors {
    /// <summary>
    /// Discovers assets with TMaps that hold references to other assets.
    /// Assumes that the Marker is a (BP) DataAsset and that TMap key is the SoftClassPath to the target. 
    /// <br/>
    /// Mod orchestrator then initiates a second pass looking only for the references
    /// </summary>
    public class ReferenceDiscoveryProcessor : IAssetProcessor {
        /// <summary>
        /// </summary>
        private readonly string _containerClassName; // e.g., "DA_ModMarker_C"
        /// <summary>
        /// 
        /// </summary>
        private readonly string _mapPropertyName;     // e.g., "ModActors"

        // Thread-safe collection for the Orchestrator to aggregate
        public ConcurrentBag<PendingBlueprintReference> DiscoveredReferences { get; } = new();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="containerClass">
        /// (partial) Class Name to check. uses .Contains(). 
        /// e.g. "DA_ModMarker_C" or "DA_ModMarker"
        /// TODO: Convert this to use Regex
        /// </param>
        /// <param name="mapProperty">
        /// Property name of the TMap containing target assets
        /// e.g. "ModActors"
        /// </param>
        public ReferenceDiscoveryProcessor(string containerClass, string mapProperty) {
            _containerClassName = containerClass;
            _mapPropertyName = mapProperty;
        }

        public void Process(ScanContext ctx, PakScanResult result) {
            var containers = ctx.Package.ExportsLazy
                .Where(e => e.Value.Class?.Name.Contains(_containerClassName) == true);

            var curPakName = ctx.PakEntry.PakFileReader.Name;
                
                    //if (pkg == null || file.Value is not FPakEntry pakEntry) return;

                    //var context = new ScanContext(provider, pkg, file.Key, pakEntry);
                    //var pakName = pakEntry.PakFileReader.Nam
            foreach (var container in containers) {
                var map = container.Value.GetOrDefault<UScriptMap>(_mapPropertyName);
                if (map == null) continue;
                var mainExport = ctx.Package.ExportsLazy.FirstOrDefault().Value;
                var base_name = (mainExport.Super ?? mainExport.Template?.Outer)?.GetPathName();
                result.AddGenericMarker(new GenericMarkerEntry {
                    AssetPath = ctx.FilePath,
                    // AssetHash = HashUtility.GetAssetHash(ctx.Provider, ctx.FilePath),
                    ClassName = mainExport?.Class?.Name ?? "Unknown",// ?? uClass.Name,
                }, base_name);

                foreach (var entry in map.Properties) {
                    if (entry.Key.GetValue(typeof(FPackageIndex)) is FPackageIndex idx) {
                        var resolved = ctx.Package.ResolvePackageIndex(idx);
                        if (resolved != null) {
                            DiscoveredReferences.Add(new PendingBlueprintReference {
                                SourceMarkerPath = ctx.FilePath,
                                SourceMarkerClassName = base_name,
                                TargetBlueprintPath = resolved.GetPathName(),
                                TargetClassName = resolved.Name.Text,
                                SourcePakFile = curPakName,
                            });
                        }
                    }
                }
            }
        }
    }
}
