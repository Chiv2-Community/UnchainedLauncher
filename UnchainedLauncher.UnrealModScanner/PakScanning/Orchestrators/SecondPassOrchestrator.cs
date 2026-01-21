using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.VirtualFileSystem;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Diagnostics;
using UnchainedLauncher.UnrealModScanner.Assets;
using UnchainedLauncher.UnrealModScanner.AssetSources;
using UnchainedLauncher.UnrealModScanner.Config;
using UnchainedLauncher.UnrealModScanner.Models.Dto;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnchainedLauncher.UnrealModScanner.Utility;
using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.PakScanning.Orchestrators;

public class SecondPassOrchestrator {
    private readonly IFileProvider _provider;
    private readonly ScanOptions _options;

    public SecondPassOrchestrator(IFileProvider provider, ScanOptions options) {
        _provider = provider;
        _options = options;
    }

    public async Task ResolveReferencesAsync(
    ConcurrentBag<PendingBlueprintReference> pendingRefs,
    ModScanResult mainResult) // Changed from PakScanResult to the top-level structure
{
        await Task.Run(() => {
            // Group by path to avoid reloading the same package multiple times
            var groupedRefs = pendingRefs.GroupBy(r => (r.TargetBlueprintPath, r.SourcePakFile));

            // We use Parallel.ForEach for high-speed resolution
            Parallel.ForEach(groupedRefs, new ParallelOptions { MaxDegreeOfParallelism = -1 }, group => {
                try {
                    var (assetPath, refPak) = group.Key;
                    var refEntry = group.First();
                    //var refEntry = pendingRefs.Where(en => en.TargetBlueprintPath == assetPath).FirstOrDefault();

                    // Identify which Pak contains this blueprint
                    // We check our provider's file index to find the source container name
                    string pakName = refEntry.SourcePakFile;
                    if (_provider.Files.TryGetValue(assetPath, out var gameFile)) {
                        // Check if the file belongs to a Vfs (Pak/IoStore)
                        if (gameFile is VfsEntry vfsEntry) {
                            // vfsEntry.Vfs is the container (FPakReader, FIoStoreReader, etc.)
                            // .Name is the full path to the .pak/.utoc file
                            pakName = Path.GetFileName(vfsEntry.Vfs.Name);
                        }
                    }

                    // GET or ADD the specific result bucket for this Pak
                    var pakBucket = mainResult.Paks.GetOrAdd(pakName, (key) => new PakScanResult {
                        PakPath = key
                    });

                    // fixme
                    // var targetMarker = pakBucket.GenericMarkers.GetOrAdd(refEntry.SourceMarkerPath, _ => new ConcurrentBag<GenericMarkerEntry>()).FirstOrDefault();
                    var marker = pakBucket.GetMarker(refEntry.SourceMarkerClassName, refEntry.SourceMarkerPath);
                    if (marker == null) {
                        Debug.WriteLine($"Could not find marker {refEntry.SourceMarkerClassName} ({refEntry.SourceMarkerPath})");
                    }
                    var newAssetPath = assetPath.Split(".").First();
                    // FIXME: can we retrieve the asset name at this point
                    if (_provider.TryLoadPackage(newAssetPath + ".uasset", out var package))
                        newAssetPath = newAssetPath + ".uasset";
                    else if (_provider.TryLoadPackage(newAssetPath + ".umap", out package))
                        newAssetPath = newAssetPath + ".uasset";
                    else {
                        Console.WriteLine($"SecondPathOrchestrator: Failed to load asset {assetPath}");
                        return;
                    }

                    var blueprintProperties = _options.MarkerProcessors
                        .FirstOrDefault()?.ReferencedBlueprintProperties ?? new();



                    var (mainExport, index) = BaseAsset.GetMainExport(package).Value;
                    // TODO: throw
                    if (mainExport == null) return;
                    var mainExportLazy = index > 0 ? package.GetExport(index) : package.ExportsLazy[0].Value;
                    var filteredProperties = new Dictionary<string, object?>();

                    var propertyMap = new Dictionary<string, FPropertyTag>();
                    if (mainExportLazy is UClass bgc) {
                        var cdo = bgc.ClassDefaultObject.Load();
                        propertyMap = propertyMap = cdo.Properties.ToDictionary(p => p.Name.Text, p => p);
                    }
                    else {
                        propertyMap = mainExportLazy.Properties.ToDictionary(p => p.Name.Text, p => p);
                    }

                    foreach (var propConfig in blueprintProperties) {
                        if (!propertyMap.TryGetValue(propConfig.Name, out var propTag) || propTag.Tag == null)
                            continue;

                        var rawValue = propTag.Tag.GetValue(propTag.Tag.GetType()) ?? propTag.Tag.GenericValue;

                        filteredProperties[propConfig.Name] = propConfig.Mode switch {
                            EExtractionMode.Json => rawValue switch {
                                UObject nestedObj => nestedObj.ToSafeJson(0, propConfig.MaxDepth),
                                _ => JsonConvert.SerializeObject(rawValue)
                            },
                            EExtractionMode.String => rawValue?.ToString() ?? "null",
                            EExtractionMode.Raw => rawValue,
                            _ => throw new ArgumentOutOfRangeException()
                        };
                    }

                    // TODO: Verify
                    var base_name = mainExportLazy.Outer.Name ?? mainExportLazy.Template?.Outer?.Name.Text;
                    var entry = GenericAssetEntry.FromSource(
                        new PackageAssetSource(package),
                        filteredProperties);
                    if (mainExportLazy is UClass uclass) {
                        var fullName = uclass.GetFullName();
                        entry.ClassPath = PackageUtility.ToGamePathName(fullName);
                    }

                    marker?.AddGenericEntry(entry);
                    pakBucket.RemoveSpecializedAsset(entry);

                    // foreach (var export in package.ExportsLazy) {
                    //     if (export.Value is not UBlueprintGeneratedClass bpc) continue;
                    //
                    //     var cdo = bpc.ClassDefaultObject.Load();
                    //     if (cdo == null) continue;
                    //
                    //     // var entry = new GenericAssetEntry {
                    //     //     AssetPath = newAssetPath,
                    //     //     ClassName = bpc.Name
                    //     // };
                    //
                    //     var filteredProperties = new Dictionary<string, object?>();
                    //
                    //     var propertyMap = cdo.Properties.ToDictionary(p => p.Name.Text, p => p);
                    //
                    //     foreach (var propConfig in blueprintProperties) {
                    //         if (!propertyMap.TryGetValue(propConfig.Name, out var propTag) || propTag.Tag == null)
                    //             continue;
                    //
                    //         var rawValue = propTag.Tag.GetValue(propTag.Tag.GetType()) ?? propTag.Tag.GenericValue;
                    //
                    //         filteredProperties[propConfig.Name] = propConfig.Mode switch {
                    //             EExtractionMode.Json => rawValue switch {
                    //                 UObject nestedObj => nestedObj.ToSafeJson(0, propConfig.MaxDepth),
                    //                 _ => JsonConvert.SerializeObject(rawValue)
                    //             },
                    //             EExtractionMode.String => rawValue?.ToString() ?? "null",
                    //             EExtractionMode.Raw => rawValue,
                    //             _ => throw new ArgumentOutOfRangeException()
                    //         };
                    //     }
                    //
                    //     var base_name = export.Value.Outer.Name ?? export.Value.Template?.Outer?.Name.Text;
                    //     // pakBucket.AddGenericEntry(entry, base_name ?? "Marker");
                    //     var entry = GenericAssetEntry.FromSource(
                    //         new PackageAssetSource(package),
                    //         filteredProperties);
                    //     
                    //     marker?.AddGenericEntry(entry);
                    //     pakBucket.RemoveEntryGlobal(entry.AssetPath);
                    //     // pakBucket.RemoveGenericEntry(base_name, entry.AssetPath);
                    //     // marker.Value.Value.FirstOrDefault().AddGenericEntry(entry);
                    // }
                }
                catch (Exception ex) {
                    System.Diagnostics.Debug.WriteLine($"Pass 2 Error: {ex.Message}");
                }
            });
        });
    }
}