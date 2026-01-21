
using CommunityToolkit.Mvvm.ComponentModel;
using System.Diagnostics;
using System.IO;
using UnchainedLauncher.UnrealModScanner.Config;
using UnchainedLauncher.UnrealModScanner.Config.Games;
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
    public ModScanResult? LastScanResult { get; private set; }
    
    public ScanOptions? LoadedConfig { get; set; }

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

        await Task.Yield();
        // 3. Execution
        
        
        var swMod = Stopwatch.StartNew();
        
        // Get map DTOs
        /*
        var options = GameScanOptions.Chivalry2 with {
            ScanFilter = new MapsOnlyScanFilter().With(new Whitelist([
                "/TO_Bridgetown.umap",
                "/TO_Bulwark.umap",
                "/To_Lionspire.umap",
                "/TO_RudhelmSiege.umap",
                "/TO_Citadel.umap",
                "/TO_Coxwell.umap",
                "/To_DarkForest.umap",
                "/TO_Galencourt.umap",
                "/TO_Montcrux.umap",
                "/TO_Library.umap",
                "/TO_Falmire.umap",
                "/TO_Raid.umap",
                "/TO_Stronghold.umap",
                "/FFA_Bazaar.umap",
                "/FFA_Courtyard.umap",
                "/FFA_Desert.umap",
                "/FFA_FightingPit.umap",
                "/FFA_FrozenWreck.umap",
                "/FFA_Galencourt.umap",
                "/FFA_Hippodrome.umap",
                "/FFA_Falmire.umap",
                "/FFA_TournamentGrounds.umap",
                "/FFA_Duelyard.umap",
                "/FFA_Wardenglade.umap",
                "/Brawl_GreatHall.umap",
                "/Brawl_RudhelmHall.umap",
                "/Brawl_Cathedral.umap",
                "/BRAWL_Midsommar.umap",
                "/BRAWL_Raid.umap",
                "/Arena_Courtyard.umap",
                "/Arena_FightingPit.umap",
                "/Arena_TournamentGrounds.umap",
                "/Arena_TournamentGrounds_Joust.umap",
                "/LTS_Courtyard.umap",
                "/LTS_FightingPit.umap",
                "/LTS_Galencourt.umap",
                "/LTS_Falmire.umap",
                "/LTS_TournamentGrounds.umap",
                "/LTS_Wardenglade.umap",
                "/TDM_Courtyard.umap",
                "/TDM_Coxwell.umap",
                "/TDM_DarkForest.umap",
                "/TDM_Desert.umap",
                "/TDM_FightingPit.umap",
                "/TDM_FrozenWreck.umap",
                "/TDM_Galencourt.umap",
                "/TDM_Hippodrome.umap",
                "/TDM_Falmire.umap",
                "/TDM_TournamentGrounds.umap",
                "/TDM_Wardenglade.umap",
                "/TDM_Wardenglade_horse.umap",
                "/BOW_Galencourt.umap",
                "/BOW_Falmire.umap",
                "/BOW_Wardenglade.umap",
                "/PR_LTS_Raid.umap"
            ]))
        };
        // dont forget to set scan mode to GameInternal.
        */

        var options = LoadedConfig ?? GameScanOptions.Chivalry2;
        var provider = FilteredFileProvider.CreateFromOptions(options, ScanMode.Mods, pakDir);
        
        var modScanner = ScannerFactory.CreateModScanner(options);
        LastScanResult = await Task.Run(() => modScanner.RunScanAsync(provider, options, progressReporter));

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

    public void ExportJson(string path, bool createManifest) {
        if (LastScanResult == null) return;
        
        if (createManifest) {
            var scanManifest = ModManifestConverter.ProcessModScan(LastScanResult);
            ModScanJsonExporter.ExportToFile(scanManifest, path);
        }
        else {
            ModScanJsonExporter.ExportToFile(LastScanResult, path);
        }
    }
}