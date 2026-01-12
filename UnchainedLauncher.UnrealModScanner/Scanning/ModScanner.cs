
using CUE4Parse.Compression;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Pak.Objects;
using CUE4Parse.UE4.Versions;
using System.Security.Cryptography;
using UnchainedLauncher.UnrealModScanner.Models;
using UnchainedLauncher.UnrealModScanner.Models.UnchainedLauncher.UnrealModScanner.Models;
using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.Scanning {
    public sealed class ModScanner {
        private readonly AssetReplacementScanner _replacementScanner;
        private readonly PackageExportScanner _exportScanner;

        public ModScanner(AssetReplacementScanner replacementScanner, PackageExportScanner exportScanner) {
            _replacementScanner = replacementScanner;
            _exportScanner = exportScanner;
        }

        public async Task<ModScanContext> ScanAsync(string pakDir) {
            return await Task.Run(() => {
                var provider = CreateProvider(pakDir);

                var mods = ScanMarkers(provider);
                var options = new AssetScannerOptions();
                var replacements = _replacementScanner.Scan(provider, options);


                options.InvertCheck = true;
                options.AssetExtensions = [".umap"];
                var custom_maps = _exportScanner.Scan(provider, options);
                var merged = PakScanResult.MergeAll(mods, replacements, custom_maps);
                //return new ModScanContext(provider, a, b);
                return new ModScanContext(provider, merged);
            });
        }

        public static string GetAssetSHA(IFileProvider provider, string path, object defaultObject) {
            if (provider.TrySaveAsset(path, out var data_pak)) {
                byte[] hashBytes = SHA1.HashData(data_pak);
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
            }
            return defaultObject.GetHashCode().ToString();
        }

        private static IFileProvider CreateProvider(string pakDir) {
            var provider = new FilteredFileProvider(
                pakDir,
                SearchOption.TopDirectoryOnly,
                true,
                new VersionContainer(EGame.GAME_UE4_25));

            provider.PakFilter = p =>
                !p.Name.EndsWith("pakchunk0-WindowsNoEditor.pak",
                    StringComparison.OrdinalIgnoreCase);

            provider.Initialize();
            provider.SubmitKey(
                new FGuid(),
                new FAesKey("0x0000000000000000000000000000000000000000000000000000000000000000"));

            provider.LoadVirtualPaths();

            ZlibHelper.DownloadDll();
            ZlibHelper.Initialize("zlib-ng2.dll");

            return provider;
        }

        //private static Dictionary<string, List<BlueprintModInfo>> ScanMarkers(IFileProvider provider) {
        private static Dictionary<string, PakScanResult> ScanMarkers(IFileProvider provider) {
            //var result = new Dictionary<string, List<BlueprintModInfo>>(); // TODO: remove
            var pakResults = new Dictionary<string, PakScanResult>();

            foreach (var file in provider.Files.Where(f => f.Key.EndsWith(".uasset"))) {
                var pkg = provider.LoadPackage(file.Key);
                if (pkg == null)
                    continue;

                var markers = pkg.ExportsLazy
                    .Where(e => e.Value.Class?.Name.Contains("DA_ModMarker_C") == true);

                if (!markers.Any())
                    continue;

                var pakEntry = (FPakEntry)file.Value;
                var pakName = pakEntry.PakFileReader.Name;
                // Fixme: save pakhash
                var pakHash = GetAssetSHA(provider, file.Key, pakEntry.PakFileReader);


                if (!pakResults.TryGetValue(pakName, out var pakResult)) {
                    pakResult = new PakScanResult {
                        PakPath = pakName,
                        PakHash = pakHash,
                    };
                    pakResults[pakName] = pakResult;
                }

                //if (!result.TryGetValue(pakName, out var list)) {
                //    list = new List<BlueprintModInfo>();
                //    result[pakName] = list;
                //}

                foreach (var marker in markers) {
                    var map = marker.Value.GetOrDefault<UScriptMap>("ModActors");
                    if (map == null)
                        continue;
                        var markerHash = GetAssetSHA(provider, pkg.Name + ".uasset", marker);

                        var markerInfo = new ModMarkerInfo {
                            MarkerAssetPath = pkg.Name,
                            MarkerAssetHash = markerHash,
                        };

                        foreach (var entry in map.Properties) {
                            if (entry.Key.GetValue(typeof(FPackageIndex)) is not FPackageIndex idx)
                                continue;

                            var resolved = pkg.ResolvePackageIndex(idx);
                            if (resolved?.Object?.Value is not UClass uClass)
                                continue;

                            var cdo = uClass.ClassDefaultObject.Load();
                            if (cdo == null)
                                continue;
                            string bpPath = resolved.GetPathName();
                            var bpHash = GetAssetSHA(provider, bpPath.Split('.')[0] + ".uasset", uClass);

                            markerInfo.Blueprints.Add(new BlueprintModInfo {
                                ModName = cdo.GetOrDefault<string>("ModName"),
                                Version = cdo.GetOrDefault<string>("ModVersion"),
                                Author = cdo.GetOrDefault<string>("Author"),
                                IsClientSide = cdo.GetOrDefault<bool>("bClientside"),
                                BlueprintPath = bpPath,
                                BlueprintHash = bpHash
                        });
                        }

                        pakResult.Markers.Add(markerInfo);
                }
            }

            return pakResults;
        }
    }
}

//using CUE4Parse.Compression;
//using CUE4Parse.Encryption.Aes;
//using CUE4Parse.UE4.Assets.Objects;
//using CUE4Parse.UE4.Objects.Core.Misc;
//using CUE4Parse.UE4.Objects.UObject;
//using CUE4Parse.UE4.Pak.Objects;
//using CUE4Parse.UE4.Versions;
//using System.Security.Cryptography;
//using UnchainedLauncher.UnrealModScanner.Models;
//using UnrealModScanner.Models;

//namespace UnchainedLauncher.UnrealModScanner {
//    public sealed class ModScanner : IModScanner {
//        public Task<IReadOnlyList<PakScanResult>> ScanAsync(
//        string pakDirectory,
//        CancellationToken cancellationToken = default) {
//            return Task.Run(() => ScanInternal(pakDirectory, cancellationToken), cancellationToken);
//        }

//        private IReadOnlyList<PakScanResult> ScanInternal(
//            string pakDirectory,
//            CancellationToken ct) {
//            var pakResults = new Dictionary<string, PakScanResult>();

//            var provider = new FilteredFileProvider(
//                pakDirectory,
//                SearchOption.TopDirectoryOnly,
//                true,
//                new VersionContainer(EGame.GAME_UE4_25));

//            provider.PakFilter = file =>
//                !file.Name.EndsWith("pakchunk0-WindowsNoEditor.pak",
//                    StringComparison.OrdinalIgnoreCase);

//            provider.Initialize();
//            provider.SubmitKey(new FGuid(),
//                new FAesKey("0x0000000000000000000000000000000000000000000000000000000000000000"));

//            provider.LoadVirtualPaths();

//            // Required for compressed cooked assets
//            ZlibHelper.DownloadDll();
//            ZlibHelper.Initialize("zlib-ng2.dll");

//            foreach (var file in provider.Files.Where(f => f.Key.EndsWith(".uasset"))) {
//                ct.ThrowIfCancellationRequested();

//                var pkg = provider.LoadPackage(file.Key);
//                if (pkg == null)
//                    continue;

//                if (pkg.Name.EndsWith("DA_ModMarker", StringComparison.OrdinalIgnoreCase))
//                    continue;

//                var pakEntry = (FPakEntry)file.Value;
//                var pakName = pakEntry.PakFileReader.Name;
//                var pakHash = pakEntry.PakFileReader.GetHashCode().ToString();
//                if (provider.TrySaveAsset(file.Key, out var data_pak)) {
//                    byte[] hashBytes = SHA1.HashData(data_pak);
//                    pakHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
//                }

//                if (!pakResults.TryGetValue(pakName, out var pakResult)) {
//                    pakResult = new PakScanResult {
//                        PakPath = pakName,
//                        PakHash = pakHash,
//                        Mods = new List<ModMarkerInfo>()
//                    };
//                    pakResults[pakName] = pakResult;
//                }

//                var markers = pkg.ExportsLazy
//                    .Where(e => e.Value.Class?.Name.Contains("DA_ModMarker_C") == true);

//                if (!markers.Any())
//                    continue;

//                var marker = markers.First();
//                var markerHash = marker.GetHashCode().ToString();

//                var map = marker.Value.GetOrDefault<UScriptMap>("ModActors");
//                if (map == null)
//                    continue;

//                foreach (var entry in map.Properties) {
//                    ct.ThrowIfCancellationRequested();

//                    var keyIndex = entry.Key.GetValue(typeof(FPackageIndex)) as FPackageIndex;
//                    if (keyIndex == null)
//                        continue;

//                    var resolvedKey = pkg.ResolvePackageIndex(keyIndex);
//                    if (resolvedKey?.Super?.Outer?.Name !=
//                        "TBL/Content/Mods/ArgonSDK/Mods/ArgonSDKModBase")
//                        continue;

//                    if (resolvedKey.Object?.Value is not UClass uClass)
//                        continue;

//                    var cdo = uClass.ClassDefaultObject.Load();
//                    if (cdo == null)
//                        continue;

//                    string objectPath = resolvedKey.GetPathName();
//                    string packagePath = objectPath.Split('.')[0];
//                    string bpHash = uClass.GetHashCode().ToString();
//                    if (provider.TrySaveAsset(packagePath + ".uasset", out var data_asset)) {
//                        byte[] hashBytes = SHA1.HashData(data_asset);
//                        bpHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();

//                        //Console.WriteLine($"Full Object Path: {objectPath}");
//                        //Console.WriteLine($"SHA1 Hash of File: {shaHash}");
//                    }

//                    var modInfo = new BlueprintModInfo {
//                        BlueprintPath = resolvedKey.GetPathName(),
//                        BlueprintHash = bpHash,
//                        ModName = cdo.GetOrDefault<string>("ModName") ?? string.Empty,
//                        Version = cdo.GetOrDefault<string>("ModVersion") ?? string.Empty,
//                        Author = cdo.GetOrDefault<string>("Author") ?? string.Empty,
//                        IsClientSide = cdo.GetOrDefault<bool>("bClientside")
//                    };

//                    if (provider.TrySaveAsset(pkg.Name + ".uasset", out var data_marker)) {
//                        byte[] hashBytes = SHA1.HashData(data_marker);
//                        markerHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
//                    }
//                    var markerInfo = new ModMarkerInfo {
//                        MarkerAssetPath = pkg.Name,
//                        MarkerAssetHash = markerHash,
//                        Description = entry.Value?.ToString() ?? string.Empty,
//                        Blueprint = modInfo
//                    };

//                    ((List<ModMarkerInfo>)pakResult.Mods).Add(markerInfo);
//                }
//            }

//            return pakResults.Values.ToList();
//        }
//    }

//}
