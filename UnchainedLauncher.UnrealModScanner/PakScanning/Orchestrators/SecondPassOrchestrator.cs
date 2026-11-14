using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.UObject;
using log4net;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using UnchainedLauncher.UnrealModScanner.Assets;
using UnchainedLauncher.UnrealModScanner.AssetSources;
using UnchainedLauncher.UnrealModScanner.Config;
using UnchainedLauncher.UnrealModScanner.Models.Dto;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnchainedLauncher.UnrealModScanner.Utility;

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

            Parallel.ForEach(groupedRefs, new ParallelOptions { MaxDegreeOfParallelism = -1 }, group => {
                var refEntry = group.First();
                using var dependencyScope = (_provider as FilteredFileProvider)?.TrackScan(refEntry.SourcePakFile);
                var assetPath = refEntry.TargetBlueprintPath;
                try {
                    // A pak inventory contains only its physical assets, not another pak's actors.
                    if (!mainResult.Paks.TryGetValue(refEntry.SourcePakFile, out var pakBucket))
                        throw new InvalidDataException("Source pak was not scanned.");
                    var marker = pakBucket.GetMarker(refEntry.SourceMarkerClassName, refEntry.SourceMarkerPath);
                    if (marker == null)
                        throw new InvalidDataException("Source marker was not found.");
                    var packagePath = assetPath.Split(".").First();
                    if (!refEntry.SourceFiles.TryGetValue(_provider.FixPath(packagePath + ".uasset"), out var gameFile)
                        && !refEntry.SourceFiles.TryGetValue(_provider.FixPath(packagePath + ".umap"), out gameFile)) {
                        Logger.Debug($"Skipping non-local actor reference '{assetPath}' in pak '{refEntry.SourcePakFile}'.");
                        return;
                    }
                    var package = _provider.LoadPackage(gameFile);

                    var blueprintProperties = _options.MarkerProcessors
                        .FirstOrDefault()?.ReferencedBlueprintProperties ?? new();

                    var (mainExport, index) = BaseAsset.GetMainExport(package)
                        ?? throw new InvalidDataException("Referenced package has no main export.");
                    if (mainExport == null) throw new InvalidDataException("Referenced package has no main export.");
                    var mainExportLazy = (index > 0 ? package.GetExport(index) : package.ExportsLazy[0].Value)
                        ?? throw new InvalidDataException("Referenced package main export could not be loaded.");
                    var filteredProperties = new Dictionary<string, object?>();

                    var properties = mainExportLazy is UClass blueprint
                        ? blueprint.ClassDefaultObject.Load()
                            ?? throw new InvalidDataException("Referenced Blueprint has no class default object.")
                        : mainExportLazy;
                    var propertyMap = properties.Properties.ToDictionary(p => p.Name.Text, p => p);

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

                    var entry = GenericAssetEntry.FromSource(
                        new PackageAssetSource(package, gameFile),
                        filteredProperties);
                    if (mainExportLazy is UClass uclass) {
                        var fullName = uclass.GetFullName();
                        entry.ClassPath = PackageUtility.ToGamePathName(fullName);
                    }

                    marker.AddGenericEntry(entry);

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