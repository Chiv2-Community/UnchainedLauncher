using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.UObject;
using UnchainedLauncher.UnrealModScanner.Models;
using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.PakScanning.Processors {
    public class MarkerProcessor : IAssetProcessor {
        public void Process(ScanContext ctx, PakScanResult result) {
            var markers = ctx.Package.ExportsLazy
                .Where(e => e.Value.Class?.Name.Contains("DA_ModMarker_C") == true);

            foreach (var marker in markers) {
                var map = marker.Value.GetOrDefault<UScriptMap>("ModActors");
                if (map == null) continue;

                var markerInfo = new ModMarkerInfo {
                    MarkerAssetPath = ctx.Package.Name,
                    MarkerAssetHash = HashUtility.GetAssetHash(ctx.Provider, ctx.FilePath, marker)
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
                            IsClientSide = cdo.GetOrDefault<bool>("bIsClientside"),
                            BlueprintPath = uClass.GetPathName(),
                            BlueprintHash = HashUtility.GetAssetHash(ctx.Provider, blueprintPath, uClass)
                        });
                    }
                }
                result._Markers.Add(markerInfo);
            }
        }
    }
}
