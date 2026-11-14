using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.UObject;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using log4net;
using UnchainedLauncher.UnrealModScanner.Assets;
using UnchainedLauncher.UnrealModScanner.AssetSources;
using UnchainedLauncher.UnrealModScanner.Config;
using UnchainedLauncher.UnrealModScanner.Models.Dto;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnchainedLauncher.UnrealModScanner.Utility;
using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.PakScanning.Orchestrators;

public class SecondPassOrchestrator {
    private static readonly ILog Logger = LogManager.GetLogger(typeof(SecondPassOrchestrator));
    private readonly IFileProvider _provider;
    private readonly ScanOptions _options;

    public SecondPassOrchestrator(IFileProvider provider, ScanOptions options) {
        _provider = provider;
        _options = options;
    }

    public async Task ResolveReferencesAsync(
    ConcurrentBag<PendingBlueprintReference> pendingRefs,
    ModScanResult mainResult, bool failOnErrors = false) {
        await Task.Run(() => {
            int failures = 0;
            // Each source marker must receive its own resolved child.
            var groupedRefs = pendingRefs.GroupBy(r => (r.TargetBlueprintPath, r.SourcePakFile,
                r.SourceMarkerPath, r.SourceMarkerClassName));

            // We use Parallel.ForEach for high-speed resolution
            Parallel.ForEach(groupedRefs, new ParallelOptions { MaxDegreeOfParallelism = -1 }, group => {
                var refEntry = group.First();
                var assetPath = refEntry.TargetBlueprintPath;
                try {
                    //var refEntry = pendingRefs.Where(en => en.TargetBlueprintPath == assetPath).FirstOrDefault();

                    // The marker belongs to the source pak, even for cross-pak references.
                    if (!mainResult.Paks.TryGetValue(refEntry.SourcePakFile, out var pakBucket))
                        throw new InvalidDataException("Source pak was not scanned.");
                    var marker = pakBucket.GetMarker(refEntry.SourceMarkerClassName, refEntry.SourceMarkerPath);
                    if (marker == null)
                        throw new InvalidDataException("Source marker was not found.");
                    var packagePath = assetPath.Split(".").First();
                    if (!_provider.TryGetGameFile(packagePath + ".uasset", out var gameFile)
                        && !_provider.TryGetGameFile(packagePath + ".umap", out gameFile)) {
                        if (group.All(reference => reference.IsInferred)) {
                            Logger.Debug($"No naming-convention target '{assetPath}' for marker '{refEntry.SourceMarkerPath}' in pak '{refEntry.SourcePakFile}'.");
                            return;
                        }
                        throw new FileNotFoundException("Declared blueprint reference was not found.", assetPath);
                    }
                    // Do not turn unreadable packages into an optional missing-reference result.
                    var package = _provider.LoadPackage(gameFile);

                    var blueprintProperties = _options.MarkerProcessors
                        .FirstOrDefault()?.ReferencedBlueprintProperties ?? new();



                    var (mainExport, index) = BaseAsset.GetMainExport(package).Value;
                    // TODO: throw
                    if (mainExport == null) throw new InvalidDataException("Referenced package has no main export.");
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

                    marker.AddGenericEntry(entry);

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
                    Interlocked.Increment(ref failures);
                    Logger.Error($"Reference scan failed: pak '{refEntry.SourcePakFile}', marker '{refEntry.SourceMarkerPath}' ({refEntry.SourceMarkerClassName}), target '{assetPath}'.", ex);
                }
            });
            if (failOnErrors && failures != 0) throw new IOException("Pak reference scan failed; incomplete results were not cached.");
        });
    }
}
