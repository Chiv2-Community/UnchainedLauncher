
using CommunityToolkit.Mvvm.ComponentModel;
using System.Diagnostics;
using UnchainedLauncher.UnrealModScanner.Config;
using UnchainedLauncher.UnrealModScanner.Export;
using UnchainedLauncher.UnrealModScanner.JsonModels;
using UnchainedLauncher.UnrealModScanner.PakScanning;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnchainedLauncher.UnrealModScanner.Services;
using UnchainedLauncher.UnrealModScanner.ViewModels;

namespace UnchainedLauncher.UnrealModScanner.GUI.ViewModels;

public partial class ModScanTabVM : ObservableObject {
    // public ObservableCollection<PakScanResult> ScanResults { get; } = new();
    // public PakScanResultVM ResultsVisual { get; set; } = new();
    [ObservableProperty]
    private PakScanResultVM _resultsVisual = new();
    public ModScanResult LastScanResult { get; private set; }

    // The technical DTO for JSON export
    [ObservableProperty]
    private ModManifest _scanManifest = new();

    [ObservableProperty]
    private double _scanProgress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotScanning))] // Updates the helper property below
    private bool _isScanning;
    public bool IsNotScanning => !IsScanning;

    public async Task ScanAsync(string pakDir) {
        IsScanning = true;
        ScanProgress = 0;

        // 1. UI Reset (Must be on UI Thread)
        // Application.Current.Dispatcher.Invoke(() => ScanResults.Clear());

        var progressReporter = new Progress<double>(percent => {
            ScanProgress = percent;
            //Debug.WriteLine($"Scan Progress: {percent:F2}%");
        });

        // 2. Configuration for Scanner
        var officialDirs = new[] {
            "Abilities","AI","Animation","Audio","Blueprint","Characters","Cinematics",
            "Collections","Config","Custom_Lens_Flare_VFX","Customization","Debug",
            "Developers","Environments","FX","Game","GameModes","Gameplay","Interactables",
            "Inventory","Localization","MapGen","Maps","MapsTest","Materials","Meshes",
            "Trailer_Cinematic","UI","Weapons","Engine","Mannequin"
        };

        await Task.Yield();
        // 3. Execution
        var swMod = Stopwatch.StartNew();
        var options_new = new ScanOptions();
        var modScanner = ScannerFactory.CreateModScanner(officialDirs, options_new);


        // Run scanner on a background thread
        LastScanResult = await Task.Run(() => modScanner.RunScanAsync(pakDir, ScanMode.ModsOnly, options_new, progressReporter));

        swMod.Stop();
        Debug.WriteLine($"Mod Scan completed in: {swMod.ElapsedMilliseconds}ms");

        // 4. Generate the Technical Manifest (The JSON model)
        ResultsVisual = new PakScanResultVM(LastScanResult);

        // 5. Build TreeView Nodes (The UI model)
        // foreach (var (pakName, scanResult) in context.Paks) {
        //     int numMods = scanResult._Markers.Sum(m => m.Blueprints.Count);
        //     int numReplacements = scanResult._AssetReplacements.Count;
        //     int numMaps = scanResult._Maps.Count;
        //     int numOther = scanResult.ArbitraryAssets.Count;
        //
        //     // Generate Summary Text
        //     var parts = new List<string>();
        //     if (numMods > 0) parts.Add($"{numMods} mods");
        //     if (numReplacements > 0) parts.Add($"{numReplacements} assets");
        //     if (numMaps > 0) parts.Add($"{numMaps} maps");
        //
        //     var summary = string.Join(", ", parts);
        //     scanResult.PakPathExpanded = $"📦 {scanResult.PakPath}" + (summary.Length > 0 ? $" ({summary})" : "");
        //     scanResult.IsExpanded = false;
        //
        //     // --- Group: Mods ---
        //     if (numMods > 0) {
        //         var modsGroup = new PakGroupNode($"Mods ({numMods})");
        //         foreach (var marker in scanResult._Markers)
        //             foreach (var mod in marker.Blueprints)
        //                 modsGroup.Children.Add(new ModTreeNode(mod));
        //
        //         scanResult.Children.Add(modsGroup);
        //     }
        //
        //     // --- Group: Asset Replacements ---
        //     if (numReplacements > 0) {
        //         var replGroup = new PakGroupNode($"Asset Replacements ({numReplacements})", false);
        //         foreach (var repl in scanResult._AssetReplacements)
        //             replGroup.Children.Add(new AssetReplacementTreeNode(repl));
        //
        //         scanResult.Children.Add(replGroup);
        //     }
        //
        //     // --- Group: Maps ---
        //     if (numMaps > 0) {
        //         var mapsGroup = new PakGroupNode($"Maps ({numMaps})");
        //         foreach (var map in scanResult._Maps) {
        //             mapsGroup.Children.Add(new AssetReplacementTreeNode(new AssetReplacementInfo {
        //                 AssetPath = map.AssetPath,
        //                 AssetHash = map.AssetHash,
        //                 ClassName = map.GameMode,
        //                 Extension = "umap"
        //             }));
        //         }
        //         scanResult.Children.Add(mapsGroup);
        //     }
        //
        //     // --- Group: Other (Arbitrary) ---
        //     if (numOther > 0) {
        //         var otherGroup = new PakGroupNode($"Other ({numOther})");
        //         foreach (var arb in scanResult.ArbitraryAssets) {
        //             otherGroup.Children.Add(new AssetReplacementTreeNode(new AssetReplacementInfo {
        //                 AssetPath = arb.AssetPath,
        //                 AssetHash = arb.AssetHash,
        //                 ClassName = arb.ModName ?? arb.ClassName
        //             }));
        //         }
        //         scanResult.Children.Add(otherGroup);
        //     }
        //
        //     // 6. Push to UI Collection
        //     Application.Current.Dispatcher.Invoke(() => ScanResults.Add(scanResult));
        // }
        IsScanning = false;
    }

    public void ExportJson(string path, bool manifest) {
        if (LastScanResult == null) return;
        ModScanJsonExporter.ExportToFile(LastScanResult, path);
        if (manifest) {
            ScanManifest = ModManifestConverter.ProcessModScan(LastScanResult);
            ModScanJsonExporter.ExportToFile(ScanManifest, path);
        }
    }
}