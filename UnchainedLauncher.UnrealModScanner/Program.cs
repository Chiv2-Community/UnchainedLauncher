using CUE4Parse.Compression;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Pak.Objects;
using CUE4Parse.UE4.Versions;
using System.IO;
using System.Security.Cryptography;
using UnchainedLauncher.UnrealModScanner.Scanning;


internal class Program {
    private static void Main(string[] args) {
        var pakdir = "I:\\Epic Games\\Chivalry2_c\\TBL\\Content\\Paks";
        //var pakdir_other = "U:\\Unchained\\Cleanup\\Unchained-Mods-internal";

        //var provider = new DefaultFileProvider(pakdir, SearchOption.TopDirectoryOnly, true, new VersionContainer(EGame.GAME_UE4_25));
        var provider = new FilteredFileProvider(pakdir, SearchOption.TopDirectoryOnly, true, new VersionContainer(EGame.GAME_UE4_25));

        provider.PakFilter = (file) => !file.Name.EndsWith("pakchunk0-WindowsNoEditor.pak", StringComparison.OrdinalIgnoreCase);
        provider.Initialize(); // will scan the archive directory for supported file extensions
        provider.SubmitKey(new FGuid(), new FAesKey("0x0000000000000000000000000000000000000000000000000000000000000000")); // decrypt basic info (1 guid - 1 key)                                          //provider.LoadLocalization(ELanguage.English); // explicit enough
        provider.LoadVirtualPaths();
        ZlibHelper.DownloadDll(); // TODO: better way?
        ZlibHelper.Initialize("zlib-ng2.dll");
        var umap_exports = provider.Files.Where(file => file.Key.Contains(".uasset"));
        foreach (var export in umap_exports) {
            //Console.WriteLine(export.Key);
            var pkg = provider.LoadPackage(export.Key);
            if (pkg.Name.EndsWith("DA_ModMarker"))
                continue;
            var pak_entry = (FPakEntry)export.Value;
            var pak_name = pak_entry.PakFileReader.Name;
            var pak_hash = pak_entry.PakFileReader.GetHashCode();
            if (provider.TrySaveAsset(export.Key, out var data_marker)) {
                byte[] hashBytes = SHA1.HashData(data_marker);
                string shaHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
                Console.WriteLine($"Asset: {export.Key} | SHA: {shaHash}");
            }

            var markers = pkg.ExportsLazy.Where(export => export.Value.Class?.Name.Contains("DA_ModMarker_C") == true);
            if (markers.Any()) {
                var marker = markers.First();
                var marker_hash = marker.GetHashCode();
                Console.WriteLine("Found {2} {0}: {1}", marker.Value.Class, pkg.Name, pak_name);
                var map = marker.Value.GetOrDefault<UScriptMap>("ModActors");
                if (map == null)
                    continue;
                foreach (var entry in map.Properties) {
                    //var softPath = (FSoftObjectPath)entry.Key.GetValue(typeof(ObjectProperty));
                    var softPath = entry.Key.GetValue(typeof(ObjectProperty));
                    var keyIndex = entry.Key.GetValue(typeof(FPackageIndex)) as FPackageIndex;

                    if (keyIndex != null) {
                        // Resolve the index to get the actual object name/path
                        var resolvedKey = pkg.ResolvePackageIndex(keyIndex);
                        if (resolvedKey?.Super?.Outer?.Name != "TBL/Content/Mods/ArgonSDK/Mods/ArgonSDKModBase")
                            continue;

                        string objectPath = resolvedKey.GetPathName();
                        string packagePath = objectPath.Split('.')[0];
                        if (provider.TrySaveAsset(packagePath + ".uasset", out var data_asset)) {
                            byte[] hashBytes = SHA1.HashData(data_asset);
                            string shaHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();

                            Console.WriteLine($"Full Object Path: {objectPath}");
                            Console.WriteLine($"SHA1 Hash of File: {shaHash}");
                        }

                        //var hash_code = resolvedKey?.Super?.Outer?.GetHashCode();

                        Console.WriteLine($"  {resolvedKey?.Name}");
                        var classObject = resolvedKey?.Object?.Value;
                        if (classObject is UClass uClass) {
                            var cdo = uClass.ClassDefaultObject.Load();
                            var cdo_hash = uClass.GetHashCode();

                            if (cdo != null) {
                                var ModName = cdo.GetOrDefault<String>("ModName");
                                var ModVersion = cdo.GetOrDefault<String>("ModVersion");
                                var Author = cdo.GetOrDefault<String>("Author");
                                var bClientside = cdo.GetOrDefault<bool>("bClientside");
                                var PathName = resolvedKey?.GetPathName();

                                Console.WriteLine("    PathName:     {0}", PathName);
                                Console.WriteLine("    ModName:      {0}", ModName);
                                Console.WriteLine("    ModVersion:   {0}", ModVersion);
                                Console.WriteLine("    Author:       {0}", Author);
                                Console.WriteLine("    bClientside:  {0}", bClientside);
                                //foreach (var prop in cdo.Properties) {
                                //    Console.WriteLine($"    Default Value -> {prop.Name}: {prop.Tag?.GenericValue}");

                                //}
                            }
                        }

                    }
                }

            }

        }

    }
}