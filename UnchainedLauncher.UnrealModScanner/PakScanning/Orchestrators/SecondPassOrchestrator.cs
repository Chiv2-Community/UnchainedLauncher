using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.VirtualFileSystem;
using Newtonsoft.Json;
using System.Collections.Concurrent;
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
            var groupedRefs = pendingRefs.GroupBy(r => r.TargetBlueprintPath);

            // We use Parallel.ForEach for high-speed resolution
            Parallel.ForEach(groupedRefs, new ParallelOptions { MaxDegreeOfParallelism = -1 }, group => {
                try {
                    var assetPath = group.Key;
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

                    // FIXME: this could be umap
                    var newAssetPath = assetPath.Split(".").First() + ".uasset";

                    if (!_provider.TryLoadPackage(newAssetPath, out var package)) {
                        Console.WriteLine($"Failed to load asset {assetPath}");
                        return;
                    }

                    var blueprintProperties = _options.MarkerProcessors
                        .FirstOrDefault()?.ReferencedBlueprintProperties ?? new();

                    foreach (var export in package.ExportsLazy) {
                        if (export.Value is not UBlueprintGeneratedClass bpc) continue;

                        var cdo = bpc.ClassDefaultObject.Load();
                        if (cdo == null) continue;

                        var entry = new GenericAssetEntry {
                            AssetPath = assetPath,
                            ClassName = bpc.Name
                        };

                        var propertyMap = cdo.Properties.ToDictionary(p => p.Name.Text, p => p);

                        foreach (var propConfig in blueprintProperties) {
                            if (!propertyMap.TryGetValue(propConfig.Name, out var propTag) || propTag.Tag == null)
                                continue;

                            var rawValue = propTag.Tag.GetValue(propTag.Tag.GetType()) ?? propTag.Tag.GenericValue;

                            // if (rawValue != null)
                            //     Console.WriteLine($"Actually got val {rawValue}");

                            entry.Properties[propConfig.Name] = propConfig.Mode switch {
                                EExtractionMode.Json => rawValue switch {
                                    UObject nestedObj => nestedObj.ToSafeJson(0, propConfig.MaxDepth),
                                    _ => JsonConvert.SerializeObject(rawValue)
                                },
                                EExtractionMode.String => rawValue?.ToString() ?? "null",
                                EExtractionMode.Raw => rawValue,
                                _ => throw new ArgumentOutOfRangeException()
                            };
                        }

                        var base_name = (export.Value.Super ?? export.Value.Template?.Outer)?.GetPathName();
                        pakBucket.AddGenericEntry(entry, base_name ?? "Marker");
                    }
                }
                catch (Exception ex) {
                    System.Diagnostics.Debug.WriteLine($"Pass 2 Error: {ex.Message}");
                }
            });
        });
    }
}