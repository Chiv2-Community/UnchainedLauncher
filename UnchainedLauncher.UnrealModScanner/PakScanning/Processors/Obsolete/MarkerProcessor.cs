using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.UObject;
using UnchainedLauncher.UnrealModScanner.Models.Chivalry2;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnchainedLauncher.UnrealModScanner.Utility;
using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.PakScanning.Processors.Obsolete {
    /// <summary>
    /// Chivalry 2 specific Marker Processor
    /// <br/>
    /// TODO: Switch to new CDO/ref scanner
    /// </summary>
    [Obsolete("Superceded by generic scanner")]
    public class MarkerProcessor : IAssetProcessor {
        public void Process(ScanContext ctx, PakScanResult result) {
            // if (ctx.FilePath.EndsWith(".umap")) return;
            var markers = ctx.Package.ExportsLazy
                .Where(e => e.Value.Class?.Name.Contains("DA_ModMarker_C") == true);

            foreach (var marker in markers) {
                try {
                    var map = marker.Value.GetOrDefault<UScriptMap>("ModActors");
                    if (map == null) continue;

                    var markerInfo = new ModMarkerInfo {
                        AssetPath = ctx.Package.Name,
                        AssetHash = HashUtility.GetAssetHash(ctx.Provider, ctx.FilePath, marker)
                    };

                    foreach (var entry in map.Properties) {
                        if (entry.Key.GetValue(typeof(FPackageIndex)) is FPackageIndex idx &&
                            ctx.Package.ResolvePackageIndex(idx)?.Object?.Value is UClass uClass) {

                            var cdo = uClass.ClassDefaultObject.Load();
                            if (cdo == null) continue;
                            string blueprintPath = uClass.GetPathName().Split('.')[0] + ".uasset";

                            markerInfo.Blueprints.Add(new BlueprintModInfo {
                                ModName = cdo.GetOrDefault<string>("ModName"),
                                Author = cdo.GetOrDefault<string>("Author"),
                                Version = cdo.GetOrDefault<string>("Version"),
                                IsClientSide = cdo.GetOrDefault<bool>("bClientside"),
                                AssetPath = uClass.GetPathName(),
                                AssetHash = HashUtility.GetAssetHash(ctx.Provider, blueprintPath, uClass)
                            });
                        }
                    }
                    result._Markers.Add(markerInfo);
                }
                catch (Exception ex) {
                    System.Diagnostics.Debug.WriteLine(
                        $"Error processing marker {marker.Value.Name}: {ex}");
                }
            }
        }
    }
}