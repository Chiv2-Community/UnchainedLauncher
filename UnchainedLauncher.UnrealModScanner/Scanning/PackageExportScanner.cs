using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Pak.Objects;
using UnchainedLauncher.UnrealModScanner.Models;
using UnchainedLauncher.UnrealModScanner.Models.UnchainedLauncher.UnrealModScanner.Models;
using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.Scanning {
    public sealed class PackageExportScanner {
        private readonly IReadOnlyList<string> _standardDirs;

        //public PackageExportScanner(IEnumerable<string> standardDirs) {
        //    _standardDirs = standardDirs
        //        .Select(d => d.Replace('\\', '/').TrimEnd('/'))
        //        .ToList();
        //}

        //public Dictionary<string, List<AssetReplacementInfo>> Scan(IFileProvider provider) {
        public Dictionary<string, PakScanResult> Scan(IFileProvider provider, AssetScannerOptions options) {
            var result = new Dictionary<string, List<AssetReplacementInfo>>();
            var fileResults = new Dictionary<string, PakScanResult>();

            var assetFiles = provider.Files
                .Where(f => options.AssetExtensions.Any(ext => f.Key.EndsWith(ext, StringComparison.OrdinalIgnoreCase)));

            foreach (var file in assetFiles) {
                var path = file.Key.Replace('\\', '/');

                if (options.InvertCheck == options.AssetDirectories.Any(dir =>
                    path.StartsWith($"TBL/Content/{dir}", StringComparison.OrdinalIgnoreCase)))
                    //path.StartsWith(dir, StringComparison.OrdinalIgnoreCase)))
                    continue;

                if (file.Value is not FPakEntry pakEntry)
                    continue;

                var pakName = pakEntry.PakFileReader.Name;
                if (!fileResults.TryGetValue(pakName, out var pakResult)) {
                    pakResult = new PakScanResult {
                        PakPath = pakName,
                        PakHash = ModScanner.GetAssetSHA(provider, file.Key, pakEntry.PakFileReader),
                    };
                    fileResults[pakName] = pakResult;
                }

                var pkg = provider.LoadPackage(file.Key);

                foreach (var exportEntry in pkg.ExportsLazy) {
                    if (exportEntry.Value == null) continue;
                    var assetPath = exportEntry.Value.GetPathName();
                    if (string.IsNullOrEmpty(assetPath)) continue;

                    var class_type = exportEntry.Value?.Class?.Name;

                    if (class_type == "WorldSettings") {
                        var modeProp = exportEntry.Value.Properties.FirstOrDefault(x => x.Name.Text == "DefaultGameMode");
                        if (modeProp?.Tag is ObjectProperty objProp) {
                            var path2 = objProp.Value.Name; // This is your asset path

                            var bpHash = ModScanner.GetAssetSHA(provider, assetPath.Split('.')[0] + ".uasset", exportEntry.Value);
                            pakResult.Maps.Add(new GameMapInfo {
                                AssetPath = path,
                                AssetHash = bpHash,
                                GameMode = path2,
                            });
                        }
                        foreach (var prop in exportEntry.Value.Properties) {
                            Console.WriteLine($"{prop.Name}: {prop.Tag}");
                        }
                    }

                    if (exportEntry.Value is UClass uClass) {
                        var cdo = uClass.ClassDefaultObject.Load();
                        var cdo_hash = uClass.GetHashCode();
                    }
                        //var hash = exportEntry.Value?.GetHashCode().ToString() ?? "";

                        // Only include assets inside the configured standard directories
                        //if (_options.AssetDirectories.Any(dir =>
                        //        assetPath.StartsWith($"TBL/Content/{dir}", StringComparison.OrdinalIgnoreCase))) {
                        //    list.Add(new ReplacementAssetInfo {
                        //        AssetPath = assetPath,
                        //        Hash = exportEntry.Value?.GetHashCode().ToString() ?? "",
                        //        ClassType = exportEntry.Value?.Class?.Name,
                        //        Description = $"Asset in {pakName}"
                        //    });
                        //}
                    }

                //list.Add(new AssetReplacementInfo {
                //    AssetPath = path,
                //    PakName = pakName,
                //    AssetHash = pakEntry.GetHashCode(),
                //    Extension = Path.GetExtension(path)
                //});
            }

            return fileResults;
        }
    }
}



//using CUE4Parse.Compression;
//using CUE4Parse.Encryption.Aes;
//using CUE4Parse.UE4.Objects.Core.Misc;
//using CUE4Parse.UE4.Pak.Objects;
//using CUE4Parse.UE4.Versions;
//using UnchainedLauncher.UnrealModScanner.Models;

//namespace UnchainedLauncher.UnrealModScanner;

//public sealed class AssetReplacementScanner {
//    private readonly ModScannerOptions _options;

//    public AssetReplacementScanner(ModScannerOptions? options = null) {
//        _options = options ?? new ModScannerOptions();
//    }

//    /// <summary>
//    /// Scans all pak files in a directory and returns a map: PakName -> list of replacement assets
//    /// </summary>
//    public Task<Dictionary<string, List<ReplacementAssetInfo>>> ScanAsync(
//        string pakDirectory, CancellationToken ct = default) {
//        return Task.Run(() => ScanInternal(pakDirectory, ct), ct);
//    }

//    private Dictionary<string, List<ReplacementAssetInfo>> ScanInternal(
//        string pakDirectory, CancellationToken ct) {
//        var result = new Dictionary<string, List<ReplacementAssetInfo>>(StringComparer.OrdinalIgnoreCase);

//        var provider = new FilteredFileProvider(
//            pakDirectory,
//            SearchOption.TopDirectoryOnly,
//            true,
//            new VersionContainer(EGame.GAME_UE4_25));

//        provider.PakFilter = file =>
//            !file.Name.EndsWith("pakchunk0-WindowsNoEditor.pak",
//                StringComparison.OrdinalIgnoreCase);// include all paks
//        provider.Initialize();
//        provider.SubmitKey(new FGuid(),
//            new FAesKey("0x0000000000000000000000000000000000000000000000000000000000000000"));
//        provider.LoadVirtualPaths();

//        ZlibHelper.DownloadDll();
//        ZlibHelper.Initialize("zlib-ng2.dll");

//        // Only consider files with extensions configured in options
//        var assetFiles = provider.Files
//            .Where(f => _options.AssetExtensions.Any(ext => f.Key.EndsWith(ext, StringComparison.OrdinalIgnoreCase)));

//        foreach (var file in assetFiles) {
//            ct.ThrowIfCancellationRequested();

//            var pkg = provider.LoadPackage(file.Key);
//            if (pkg == null) continue;

//            var pakEntry = (FPakEntry)file.Value;
//            var pakName = pakEntry.PakFileReader.Name;

//            if (!result.TryGetValue(pakName, out var list)) {
//                list = new List<ReplacementAssetInfo>();
//                result[pakName] = list;
//            }

//            foreach (var exportEntry in pkg.ExportsLazy) {
//                ct.ThrowIfCancellationRequested();

//                var assetPath = exportEntry.Value?.GetPathName();
//                if (string.IsNullOrEmpty(assetPath)) continue;

//                // Only include assets inside the configured standard directories
//                if (_options.AssetDirectories.Any(dir =>
//                        assetPath.StartsWith($"TBL/Content/{dir}", StringComparison.OrdinalIgnoreCase))) {
//                    list.Add(new ReplacementAssetInfo {
//                        AssetPath = assetPath,
//                        Hash = exportEntry.Value?.GetHashCode().ToString() ?? "",
//                        ClassType = exportEntry.Value?.Class?.Name,
//                        Description = $"Asset in {pakName}"
//                    });
//                }
//            }
//        }

//        return result;
//    }
//}
