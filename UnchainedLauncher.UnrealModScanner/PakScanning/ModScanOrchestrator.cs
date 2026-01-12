using CUE4Parse.Compression;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.AssetRegistry;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Pak.Objects;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;
using System.Collections.Concurrent;
using System.IO;
using UnchainedLauncher.UnrealModScanner.Models;
using UnchainedLauncher.UnrealModScanner.Scanning;
using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.PakScanning;

public class ModScanOrchestrator {
    private readonly List<IAssetProcessor> _modProcessors = new();
    private IAssetProcessor _internalProcessor;
    private const string MainPakName = "pakchunk0-WindowsNoEditor.pak";

    public void AddModProcessor(IAssetProcessor p) => _modProcessors.Add(p);
    public void SetInternalProcessor(IAssetProcessor p) => _internalProcessor = p;

    private IFileProvider CreateProvider(string pakDir, ScanMode mode) {
        var provider = new FilteredFileProvider(pakDir, SearchOption.TopDirectoryOnly, true, new VersionContainer(EGame.GAME_UE4_25));

        provider.PakFilter = p => {
            bool isMain = p.Name.Contains(MainPakName, StringComparison.OrdinalIgnoreCase);
            // If ModsOnly: Skip main pak. If GameInternal: ONLY include main pak.
            return mode == ScanMode.ModsOnly ? !isMain : isMain;
        };

        provider.Initialize();
        provider.SubmitKey(new FGuid(), new FAesKey("0x0000000000000000000000000000000000000000000000000000000000000000")); // Your key here
        provider.LoadVirtualPaths();
        ZlibHelper.DownloadDll(); // TODO: better way?
        ZlibHelper.Initialize("zlib-ng2.dll");
        return provider;
    }

    public async Task<ModScanResult> RunScanAsync(string directory, ScanMode mode, IProgress<double> progress = null) {
        return await Task.Run(() => {
            // Use 'using' or manual dispose if FilteredFileProvider supports it
            var provider = CreateProvider(directory, mode);
            var scanResult = new ModScanResult();

            var files = provider.Files
                .Where(f => f.Key.EndsWith(".uasset") || f.Key.EndsWith(".umap"))
                .ToList();

            if (files.Count == 0) {
                progress?.Report(100);
                return scanResult;
            }

            int processed = 0;
            var partitioner = Partitioner.Create(files, EnumerablePartitionerOptions.NoBuffering);
            Parallel.ForEach(partitioner, new ParallelOptions { MaxDegreeOfParallelism = -1 }, file => {
            //Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = -1 }, file => {
                try {
                    // Try to peek and skip if not relevant
                    //if (!provider.TrySaveAsset(file.Key, out var data)) return;

                    //string rawContent = System.Text.Encoding.ASCII.GetString(data, 0, Math.Min(data.Length, 5000));
                    //bool isInteresting = rawContent.Contains("Marker") || rawContent.Contains("Settings");

                    //if (!isInteresting && mode == ScanMode.ModsOnly) return; // Skip the heavy LoadPackage

                    // Normal scan
                    var pkg = provider.LoadPackage(file.Key);
                    if (pkg == null || file.Value is not FPakEntry pakEntry) return;

                    var context = new ScanContext(provider, pkg, file.Key, pakEntry);
                    var pakName = pakEntry.PakFileReader.Name;

                    // ConcurrentDictionary handles the thread-safety here
                    var result = scanResult.Paks.GetOrAdd(pakName, name => new PakScanResult {
                        PakPath = name,
                        // Optional: Initialize PakHash here if needed for ModMode
                    });

                    if (mode == ScanMode.GameInternal) {
                        _internalProcessor?.Process(context, result);
                    }
                    else {
                        foreach (var processor in _modProcessors) {
                            processor.Process(context, result);
                        }
                    }
                }
                catch (Exception ex) {
                    // Log the specific file that failed
                    System.Diagnostics.Debug.WriteLine($"Error parsing {file.Key}: {ex.Message}");
                }
                finally {
                    var count = Interlocked.Increment(ref processed);
                    if (count % 50 == 0 || count == files.Count) {
                        progress?.Report((double)count / files.Count * 100);
                    }
                }
            });

            return scanResult;
        });
    }
    public async Task<List<GameInternalAssetInfo>> QuickSearchMainPak(string pakDir) {
        return await Task.Run(() => {
            // Mode needs to be GameInternal to target the main pak logic
            var provider = CreateProvider(pakDir, ScanMode.GameInternal);
            var results = new List<GameInternalAssetInfo>();

            // Look for the registry - TBL usually keeps it in the root or /Content/
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