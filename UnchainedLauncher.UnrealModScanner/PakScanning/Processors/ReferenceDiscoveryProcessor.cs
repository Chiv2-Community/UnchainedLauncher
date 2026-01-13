using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.UObject;
using System.Collections.Concurrent;
using UnchainedLauncher.UnrealModScanner.Models.Dto;
using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.PakScanning.Processors {
    public class ReferenceDiscoveryProcessor : IAssetProcessor {
        private readonly string _containerClassName; // e.g., "DA_ModMarker_C"
        private readonly string _mapPropertyName;     // e.g., "ModActors"

        // Thread-safe collection for the Orchestrator to aggregate
        public ConcurrentBag<PendingBlueprintReference> DiscoveredReferences { get; } = new();

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

                foreach (var entry in map.Properties) {
                    if (entry.Key.GetValue(typeof(FPackageIndex)) is FPackageIndex idx) {
                        var resolved = ctx.Package.ResolvePackageIndex(idx);
                        if (resolved != null) {
                            DiscoveredReferences.Add(new PendingBlueprintReference {
                                SourceMarkerPath = ctx.FilePath,
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
