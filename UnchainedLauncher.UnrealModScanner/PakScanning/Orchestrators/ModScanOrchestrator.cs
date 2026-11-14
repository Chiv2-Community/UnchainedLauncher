using CUE4Parse.Compression;
using CUE4Parse.UE4.AssetRegistry;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Pak.Objects;
using CUE4Parse.UE4.Readers;
using log4net;
using System.Collections.Concurrent;
using UnchainedLauncher.UnrealModScanner.Assets;
using UnchainedLauncher.UnrealModScanner.Config;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnchainedLauncher.UnrealModScanner.PakScanning.Processors;

namespace UnchainedLauncher.UnrealModScanner.PakScanning.Orchestrators;

public class ModScanOrchestrator(List<IAssetProcessor> modProcessors) {
    private static readonly ILog Logger = LogManager.GetLogger(typeof(ModScanOrchestrator));

    private void InitializeProvider(FilteredFileProvider provider) {
        provider.Initialize();
        InitializeCompression(provider);
    }

    internal static void InitializeCompression(FilteredFileProvider provider) {
        // Empty/filtered-out directories need neither decompression nor a native DLL download.
        if (!provider.FilteredFiles.Any()) return;

        var zlib_path = OperatingSystem.IsLinux() ? "libz-ng.so" : "zlib-ng2.dll";
        var zlib_url = $"https://github.com/NotOfficer/Zlib-ng.NET/releases/download/1.0.0/{zlib_path}";
        if (!File.Exists(zlib_path)) {
            Console.WriteLine($"Downloading {zlib_url}");
            ZlibHelper.DownloadDll(zlib_path, zlib_url); // TODO: better way?
        }
        ZlibHelper.Initialize(Path.Combine(Directory.GetCurrentDirectory(), zlib_path));
        provider.LoadVirtualPaths();
    }

    public async Task<ModScanResult> RunScanAsync(FilteredFileProvider provider, ScanOptions options, IProgress<double> progress = null,
        Func<string, string>? hashPak = null, bool failOnErrors = false,
        Action<string, IReadOnlyList<string>?>? onVersionsScanned = null) {
        var discoveryProcessor = modProcessors.OfType<ReferenceDiscoveryProcessor>().FirstOrDefault();
        return await Task.Run(async () => {
            InitializeProvider(provider);
            var mainResult = new ModScanResult(hashPak);
            int processed = 0;
            int failures = 0;

            var files = provider.FilteredFiles.ToList();
            var versionPackages = new ConcurrentDictionary<string, ConcurrentBag<IPackage>>(StringComparer.OrdinalIgnoreCase);

            await Parallel.ForEachAsync(
                files,
                new ParallelOptions { MaxDegreeOfParallelism = -1 },
                async (file, _) => {
                    if (file.Value is not FPakEntry pakEntry)
                        return;
                    using var dependencyScope = provider.TrackScan(pakEntry.PakFileReader.Name);
                    try {
                        var pkg = await provider.LoadPackageAsync(file.Value);
                        if (pkg == null) throw new InvalidDataException("Package loader returned no package.");
                        if (onVersionsScanned != null && file.Key.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                            && provider.Files.TryGetValue(file.Key, out var selected) && ReferenceEquals(selected, file.Value))
                            versionPackages.GetOrAdd(pakEntry.PakFileReader.Name, _ => new()).Add(pkg);

                        var context = new ScanContext(provider, pkg, file.Key, pakEntry);
                        var currentPakResult = mainResult.GetOrAddPak(pakEntry.PakFileReader);

                        foreach (var processor in modProcessors) {
                            try {
                                processor.Process(context, currentPakResult);
                            }
                            catch (Exception ex) {
                                Interlocked.Increment(ref failures);
                                Logger.Error($"Processor '{processor.GetType().Name}' failed: pak '{pakEntry.PakFileReader.Name}', asset '{file.Key}'.", ex);
                            }
                        }
                        if (context.ProcessingErrors.Count > 0) {
                            Interlocked.Increment(ref failures);
                            foreach (var error in context.ProcessingErrors)
                                Logger.Error($"Processor '{error.ProcessorName}' failed: pak '{pakEntry.PakFileReader.Name}', asset '{error.PackagePath}': {error.ExceptionType}: {error.Message}");
                        }
                    }
                    catch (Exception ex) {
                        Interlocked.Increment(ref failures);
                        Logger.Error($"Package scan failed: pak '{pakEntry.PakFileReader.Name}', asset '{file.Key}'.", ex);
                    }
                    finally {
                        var count = Interlocked.Increment(ref processed);
                        if (count % 50 == 0 || count == files.Count) {
                            progress?.Report((double)count / files.Count * 100);
                        }
                    }
                });

            if (discoveryProcessor?.DiscoveredReferences.Any() == true) {
                var secondPass = new SecondPassOrchestrator(provider, options);
                await secondPass.ResolveReferencesAsync(discoveryProcessor.DiscoveredReferences, mainResult, failOnErrors);
            }

            // Replacing bags while parallel workers add/remove assets loses updates.
            // Resolve overlaps only after both passes have finished.
            RemoveSpecializedAssets(mainResult);
            if (failOnErrors && failures != 0) throw new IOException("Pak scan failed; incomplete results were not cached.");
            if (onVersionsScanned != null && failures == 0) {
                var versionReader = new BlueprintVersionScanner.Reader(versionPackages.Values.SelectMany(packages => packages).OfType<Package>());
                foreach (var (name, packages) in versionPackages) {
                    using var dependencyScope = provider.TrackScan(name);
                    IReadOnlyList<string>? versions;
                    try {
                        versions = versionReader.ReadVersions(packages.Select(package => package as Package
                            ?? throw new IOException($"Unsupported mod version package: {package.Name}")).ToArray());
                    }
                    catch (Exception error) {
                        Logger.Warn($"Could not extract Blueprint versions during full scan of pak '{name}'.", error);
                        versions = null;
                    }
                    onVersionsScanned(name, versions);
                }
            }
            return mainResult;
        });
    }

    internal static void RemoveSpecializedAssets(ModScanResult result) {
        foreach (var pak in result.Paks.Values) {
            foreach (var marker in pak.GenericMarkers.Values.SelectMany(markers => markers.Values)) {
                pak.RemoveSpecializedAsset(marker);
                foreach (var child in marker.Children) {
                    pak.RemoveSpecializedAsset(child);
                }
            }
        }
    }

    public async Task<List<GameInternalAssetInfo>> QuickSearchMainPak(FilteredFileProvider provider) {
        return await Task.Run(() => {
            // Mode needs to be GameInternal to target the main pak logic
            InitializeProvider(provider);
            var results = new List<GameInternalAssetInfo>();

            // Look for the registry - TBL usually keeps it in the root or /Content/
            // FIXME: Use game name from config
            if (provider.TrySaveAsset("TBL/AssetRegistry.bin", out var data)) {
                var archive = new FByteArchive("AssetRegistry.bin", data);

                var registryState = new FAssetRegistryState(archive);

                foreach (var asset in registryState.PreallocatedAssetDataBuffers) {
                    results.Add(new GameInternalAssetInfo {
                        AssetPath = asset.ObjectPath,
                        ClassName = asset.AssetClass.Text,
                        FullPackageName = asset.PackageName.Text,
                        AssetData = asset,
                    });
                }
            }
            return results;
        });
    }
}