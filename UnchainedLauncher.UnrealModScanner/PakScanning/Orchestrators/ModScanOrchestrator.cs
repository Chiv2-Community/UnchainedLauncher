using CUE4Parse.Compression;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.AssetRegistry;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Pak.Objects;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;
using log4net;
using Serilog;
using System.Collections.Concurrent;
using UnchainedLauncher.UnrealModScanner.Assets;
using UnchainedLauncher.UnrealModScanner.Config;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnchainedLauncher.UnrealModScanner.PakScanning.Processors;
using UnchainedLauncher.UnrealModScanner.Utility;
using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.PakScanning.Orchestrators;

public class ModScanOrchestrator {
    private readonly List<IAssetProcessor> _modProcessors = new();
    private IAssetProcessor _internalProcessor;
    private const string MainPakName = "pakchunk0-WindowsNoEditor.pak";
    private static readonly ILog Logger = LogManager.GetLogger(typeof(ModScanOrchestrator));
    public void AddModProcessor(IAssetProcessor p) => _modProcessors.Add(p);
    public void SetInternalProcessor(IAssetProcessor p) => _internalProcessor = p;

    private IFileProvider CreateProvider(string pakDir, ScanMode mode) {
        var provider = new FilteredFileProvider(pakDir, SearchOption.TopDirectoryOnly, true, new VersionContainer(EGame.GAME_UE4_25));

        provider.PakFilter = p => {
            // Filter moved to file selection
            // bool isMain = p.Name.Contains(MainPakName, StringComparison.OrdinalIgnoreCase);
            // // If ModsOnly: Skip main pak. If GameInternal: ONLY include main pak.
            // return mode == ScanMode.ModsOnly ? !isMain : isMain;
            return true;
        };

        provider.Initialize();
        provider.SubmitKey(new FGuid(), new FAesKey("0x0000000000000000000000000000000000000000000000000000000000000000")); // Your key here
        provider.LoadVirtualPaths();

        var zlib_path = OperatingSystem.IsLinux() ? "libz-ng.so" : "zlib-ng2.dll";
        var zlib_url = $"https://github.com/NotOfficer/Zlib-ng.NET/releases/download/1.0.0/{zlib_path}";
        if (!File.Exists(zlib_path)) {
            Console.WriteLine($"Downloading {zlib_url}");
            ZlibHelper.DownloadDll(zlib_path, zlib_url); // TODO: better way?
        }
        ZlibHelper.Initialize(Path.Combine(Directory.GetCurrentDirectory(), zlib_path));
        return provider;
    }

    public async Task<ModScanResult> RunScanAsync(string directory, ScanMode mode, ScanOptions options, IProgress<double> progress = null) {
        var discoveryProcessor = _modProcessors.OfType<ReferenceDiscoveryProcessor>().FirstOrDefault();
        int processed = 0;

        return await Task.Run(async () => {
            var provider = CreateProvider(directory, mode);
            //var scanResult = new ModScanResult();
            var mainResult = new ModScanResult(); // Das Top-Level Objekt
            //var scanResult = new PakScanResult();

            // 1. Apply Path Filtering
            var files = provider.Files.Where(f => {
                bool isAsset = f.Key.EndsWith(".uasset") || f.Key.EndsWith(".umap");
                if (!isAsset) return false;

                // good idea to check here?
                if (f.Value is FPakEntry fPakEntry) {
                    if (fPakEntry.PakFileReader.Name == "pakchunk0-WindowsNoEditor.pak")
                        return false;
                }
                return true;
                // if (options.PathFilters.Count == 0) return true;

                // bool matchesFilter = options.PathFilters.Any(filter => f.Key.Contains(filter, StringComparison.OrdinalIgnoreCase));
                // return options.IsWhitelist ? matchesFilter : !matchesFilter;
            }).ToList();

            // 2. Load-Balanced Partitioning
            // Setting loadBalance to 'true' enables a dynamic worker-stealing-like behavior
            var partitioner = Partitioner.Create(files, loadBalance: true);

            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = -1 };
            await Parallel.ForEachAsync<KeyValuePair<string, GameFile>>(
                files,
                parallelOptions,
                async (file, ct) => { // 'ct' ist das CancellationToken

                    if (file.Value is not FPakEntry pakEntry)
                        return;
                    IPackage? pkg = null;
                    bool packageLoaded;
                    try {
                        pkg = provider.LoadPackage(file.Key);
                    }
                    catch (Exception e) {
                        Log.Error($"Failed to load package: {e.Message}");
                        throw;
                    }
                    if (pkg == null) return;

                    try {
                        var context = new ScanContext(provider, pkg, file.Key, pakEntry);
                        var pakName = pakEntry.PakFileReader.Name;

                        // ConcurrentDictionary handles the thread-safety here
                        //var result = scanResult.Paks.GetOrAdd(pakName, name => new PakScanResult {
                        //    PakPath = name,
                        //    PakHash = HashUtility.CalculatePakHash(pakEntry.PakFileReader.Path)
                        //});
                        var currentPakResult = mainResult.Paks.GetOrAdd(pakName, (name) => new PakScanResult {
                            PakPath = name,
                            PakHash = HashUtility.CalculatePakHash(pakEntry.PakFileReader.Path)
                        });

                        if (mode == ScanMode.GameInternal) {
                            _internalProcessor?.Process(context, currentPakResult);
                        }
                        else {
                            foreach (var processor in _modProcessors) {
                                try {
                                    processor.Process(context, currentPakResult);

                                }
                                catch (Exception ex) {
                                    System.Diagnostics.Debug.WriteLine($"Processor Error: {ex.Message}");
                                }
                            }
                        }
                    }
                    catch (Exception ex) {
                        System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
                    }
                    finally {
                        var count = Interlocked.Increment(ref processed);
                        if (count % 50 == 0 || count == files.Count) {
                            progress?.Report((double)count / files.Count * 100);
                        }
                    }
                });

            if (discoveryProcessor?.DiscoveredReferences.Any() == true) {
                var secondPass = new SecondPassOrchestrator(provider, options); // options statt _options
                await secondPass.ResolveReferencesAsync(discoveryProcessor.DiscoveredReferences, mainResult);
            }

            //if (discoveryProcessor.DiscoveredReferences.Any()) {
            //    var secondPass = new SecondPassOrchestrator(provider, _options);

            //    // We aggregate results into the existing PakScanResult objects
            //    // mapped by the Blueprint's owning Pak/Path
            //    await secondPass.ResolveReferencesAsync(
            //        discoveryProcessor.DiscoveredReferences,
            //        scanResult
            //    );
            //}

            return mainResult;
        });
    }

    //public async Task<ModScanResult> RunScanAsync(string directory, ScanMode mode, IProgress<double> progress = null) {
    //    return await Task.Run(() => {
    //        // Use 'using' or manual dispose if FilteredFileProvider supports it
    //        var provider = CreateProvider(directory, mode);
    //        var scanResult = new ModScanResult();

    //        var files = provider.Files
    //            .Where(f => f.Key.EndsWith(".uasset") || f.Key.EndsWith(".umap"))
    //            .ToList();

    //        if (files.Count == 0) {
    //            progress?.Report(100);
    //            return scanResult;
    //        }

    //        int processed = 0;
    //        var partitioner = Partitioner.Create(files, EnumerablePartitionerOptions.NoBuffering);
    //        Parallel.ForEach(partitioner, new ParallelOptions { MaxDegreeOfParallelism = -1 }, file => {
    //            //Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = -1 }, file => {
    //            try {
    //                // Try to peek and skip if not relevant
    //                //if (!provider.TrySaveAsset(file.Key, out var data)) return;

    //                //string rawContent = System.Text.Encoding.ASCII.GetString(data, 0, Math.Min(data.Length, 5000));
    //                //bool isInteresting = rawContent.Contains("Marker") || rawContent.Contains("Settings");

    //                //if (!isInteresting && mode == ScanMode.ModsOnly) return; // Skip the heavy LoadPackage

    //                // Normal scan
    //                var pkg = provider.LoadPackage(file.Key);
    //                if (pkg == null || file.Value is not FPakEntry pakEntry) return;

    //                var context = new ScanContext(provider, pkg, file.Key, pakEntry);
    //                var pakName = pakEntry.PakFileReader.Name;

    //                // ConcurrentDictionary handles the thread-safety here
    //                var result = scanResult.Paks.GetOrAdd(pakName, name => new PakScanResult {
    //                    PakPath = name,
    //                    PakHash = HashUtility.CalculatePakHash(pakEntry.PakFileReader.Path)
    //                });

    //                if (mode == ScanMode.GameInternal) {
    //                    _internalProcessor?.Process(context, result);
    //                }
    //                else {
    //                    foreach (var processor in _modProcessors) {
    //                        processor.Process(context, result);
    //                    }
    //                }
    //            }
    //            catch (Exception ex) {
    //                // Log the specific file that failed
    //                System.Diagnostics.Debug.WriteLine($"Error parsing {file.Key}: {ex.Message}");
    //            }
    //            finally {
    //                var count = Interlocked.Increment(ref processed);
    //                if (count % 50 == 0 || count == files.Count) {
    //                    progress?.Report((double)count / files.Count * 100);
    //                }
    //            }
    //        });

    //        if (discoveryProcessor.DiscoveredReferences.Any()) {
    //            var secondPass = new SecondPassOrchestrator(provider, _options);

    //            // We aggregate results into the existing PakScanResult objects
    //            // mapped by the Blueprint's owning Pak/Path
    //            await secondPass.ResolveReferencesAsync(
    //                discoveryProcessor.DiscoveredReferences,
    //                scanResult
    //            );
    //        }

    //        return scanResult;
    //    });
    //}
    public async Task<List<GameInternalAssetInfo>> QuickSearchMainPak(string pakDir) {
        return await Task.Run(() => {
            // Mode needs to be GameInternal to target the main pak logic
            var provider = CreateProvider(pakDir, ScanMode.GameInternal);
            var results = new List<GameInternalAssetInfo>();

            // Look for the registry - TBL usually keeps it in the root or /Content/
            // FIXME: Use game name from config
            if (provider.TrySaveAsset("TBL/AssetRegistry.bin", out var data)) {
                var archive = new FByteArchive("AssetRegistry.bin", data);

                // This calls the constructor you provided
                var registryState = new FAssetRegistryState(archive);

                // Using the specific buffer names from your source
                foreach (var asset in registryState.PreallocatedAssetDataBuffers) {
                    results.Add(new GameInternalAssetInfo {
                        // asset.ObjectPath is an FName or FSoftObjectPath in CUE4Parse
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