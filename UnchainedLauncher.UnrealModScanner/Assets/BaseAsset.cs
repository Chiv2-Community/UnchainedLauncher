using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.UObject;
using Newtonsoft.Json;
using Serilog;
using System.Diagnostics;
using UnchainedLauncher.UnrealModScanner.AssetSources;

namespace UnchainedLauncher.UnrealModScanner.Assets {
    public class BaseAsset {
        /// <summary>
        /// Full Unreal asset path including extension
        /// e.g. TBL/Content/Characters/Knight/Knight.uasset
        /// </summary>
        [JsonProperty("asset_path", Order = -2)]
        public string AssetPath { get; set; } = string.Empty;

        [JsonProperty("class_path", Order = -2)]
        public string ClassPath { get; set; } = null;

        /// <summary>
        /// Class Name of this asset (PathName)
        /// </summary>
        [JsonProperty("class_name", Order = -3)]
        public string ClassName { get; set; } = string.Empty;

        /// <summary>
        /// Hash of the pak entry (or file hash if available)
        /// </summary>
        [JsonProperty("asset_hash", Order = 100)]
        public string AssetHash { get; set; } = string.Empty;

        public static UObject? FindMainExport(IPackage package) {
            // TODO: can iterate Exportmap here?
            foreach (var lazy in package.ExportsLazy) {
                UObject obj;
                try {
                    obj = lazy.Value;
                }
                catch {
                    // Corrupt export, skip
                    continue;
                }
                if (obj.Outer is null)
                    return obj;
            }
            return null;
        }


        public static (FObjectExport Export, int ExportIndex)? GetMainExport(IPackage package) {

            //var typed_outer = package.GetExports();
            var pkg = package as Package;
            //var nulled = pkg.ExportMap.Where(o => o.OuterIndex.IsNull).FirstOrDefault();
            if (package.ExportMapLength == 1)
                return (pkg.ExportMap[0], 0);

            for (int i = 0; i < package.ExportMapLength; i++) {
                try {

                    if (pkg.ExportMap[i].OuterIndex.IsNull)
                        return (pkg.ExportMap[i], i);

                    // var _exp = pkg.ExportMap[i];
                    // if (!_exp.IsAsset)
                    //     continue;

                    var index = i;
                }
                catch (Exception e) {
                    Log.Error(e.Message);
                    throw;
                }
            }

            return (null, 0);
        }


        protected void Initialize(IAssetSource source) {
            var filePath = source.FilePath;
            if (source is PackageAssetSource package)
                filePath = filePath + ".uasset"; // FIXME: maybe also umap? where is it stored
            // if (filePath.EndsWith(".uasset") || filePath.EndsWith(".umap")) {
            //     filePath = filePath.Replace(".uasset", "");
            //     filePath = filePath.Replace(".umap", "");
            // }
            AssetPath = filePath;

            AssetHash = source.GetHash(null);

            var (mainExport, index) = GetMainExport(source.Package).Value;
            if (mainExport is null) {
                Debug.WriteLine($"Failed to get mainexport for {source.Package.Name}");
                return;
            }
            if (mainExport.ClassIndex is null) {
                Debug.WriteLine($"Failed to get mainexport for {source.Package.Name}: ClassIndex is null");
                return;
            }

            ClassName = mainExport.ClassIndex.Name;
            if (ClassName.EndsWith("BlueprintGeneratedClass")) {
                if (!mainExport.SuperIndex.IsNull)
                    ClassName = mainExport.SuperIndex.Name;
            }

        }

    }
}