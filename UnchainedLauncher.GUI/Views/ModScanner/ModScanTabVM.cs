using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using UnchainedLauncher.UnrealModScanner.Models.UnchainedLauncher.UnrealModScanner.Models;
using UnchainedLauncher.UnrealModScanner.Scanning;
using UnchainedLauncher.UnrealModScanner.ViewModels.Nodes;
using UnrealModScanner.Export;
using UnrealModScanner.Models;

public sealed class ModScanTabVM : ObservableObject {
    private readonly ModScanner _scanner;

    public ObservableCollection<PakScanResult> ScanResults { get; } = new();

    public ModScanTabVM(ModScanner scanner) {
        _scanner = scanner;
    }
    public async Task ScanAsync(string pakDir) {
        // Clear old results
        ScanResults.Clear();

        // Run your scanner
        var context = await _scanner.ScanAsync(pakDir);

        foreach (var (pakName, scanResult) in context.Merged) {

            var num_mods = scanResult.Markers.Sum(m => m.Blueprints.Length());
            var num_replacements = scanResult.AssetReplacements.Length();
            var num_maps = scanResult.Maps.Length();

            var parts = new List<string>();
            if (num_mods > 0)
                parts.Add($"{num_mods} mods");
            if (num_replacements > 0)
                parts.Add($"{num_replacements} assets");
            if (num_maps > 0)
                parts.Add($"{num_maps} maps");
            var summary = string.Join(", ", parts);
            var collapsedText = $"📦 {scanResult.PakPath}" + (summary.Length > 0 ? $" ({summary})" : "");

            // Mods group
            var modsGroup = new PakGroupNode(string.Format("Mods ({0})", num_mods));

            foreach (var marker in scanResult.Markers)
                foreach (var mod in marker.Blueprints)
                    modsGroup.Children.Add(new ModTreeNode(mod));

            if (modsGroup.Children.Count > 0)
                scanResult.Children.Add(modsGroup);

            // Asset replacements group
            var replacementsGroup = new PakGroupNode(string.Format("Asset Replacements ({0})", num_replacements), false);

            foreach (var repl in scanResult.AssetReplacements)
                replacementsGroup.Children.Add(new AssetReplacementTreeNode(repl));

            if (replacementsGroup.Children.Count > 0)
                scanResult.Children.Add(replacementsGroup);

            // Asset replacements group
            var mapsGroup = new PakGroupNode(string.Format("Maps ({0})", num_maps));

            foreach (var map in scanResult.Maps)
                mapsGroup.Children.Add(new AssetReplacementTreeNode(
                    new AssetReplacementInfo {
                        AssetHash = map.AssetHash,
                        AssetPath = map.AssetPath,
                        ClassType = map.GameMode,
                        Extension = "umap",
                        IsReplacement = false,
                        PakName = pakName,
                    }));

            if (mapsGroup.Children.Count > 0)
                scanResult.Children.Add(mapsGroup);

            scanResult.PakPathExpanded = collapsedText;
            scanResult.IsExpanded = false;

            ScanResults.Add(scanResult);
        }

        //foreach (var (pakName, scanResult) in context.Merged) {
        //    //var result = new PakScanResult { PakPath = pakName };

        //    foreach (var marker in scanResult.Markers)
        //        foreach (var mod in marker.Blueprints)
        //            scanResult.Children.Add(new ModTreeNode(mod));

        //    foreach (var repl in scanResult.AssetReplacements)
        //        scanResult.Children.Add(new AssetReplacementTreeNode(repl));

        //    //result = new PakScanResult { PakPath = pakName };
        //    var replacementsGroup = new PakGroupNode("Asset Replacements");

        //    foreach (var map in scanResult.Maps)
        //        replacementsGroup.Children.Add(new AssetReplacementTreeNode(new AssetReplacementInfo {
        //            AssetHash = map.AssetHash,
        //            AssetPath = map.AssetPath,
        //            ClassType = map.GameMode,
        //            Extension = "umap",
        //            IsReplacement = false,
        //            PakName = pakName,
        //        }));

        //    if(replacementsGroup.Children.Count > 0)
        //        scanResult.Children.Add(replacementsGroup);
        //    ScanResults.Add(scanResult);
        //}

        return;

        // Loop over all paks that have mods
        foreach (var (pakName, mods) in context.ModsByPak) {
            var result = new PakScanResult { PakPath = pakName };

            // Add mods as child nodes
            foreach (var mod in mods)
                result.Children.Add(new ModTreeNode ( mod ));

            // Add replacements as child nodes if any
            if (context.ReplacementsByPak.TryGetValue(pakName, out var repls)) {
                foreach (var r in repls)
                    result.Children.Add(new AssetReplacementTreeNode (  r ));
            }

            // Add the PakScanResult to the observable collection bound to TreeView
            ScanResults.Add(result);
        }

        // Add paks that only have replacements (no mods)
        foreach (var (pakName, repls) in context.ReplacementsByPak) {
            // Skip paks already added above
            if (ScanResults.Any(r => r.PakPath == pakName))
                continue;

            var result = new PakScanResult { PakPath = pakName };

            foreach (var r in repls)
                result.Children.Add(new AssetReplacementTreeNode (r));

            ScanResults.Add(result);
        }
    }


    //public async Task ScanAsync(string pakDir) {
    //    // Clear previous results
    //    ScanResults.Clear();

    //    // Run the scan (mod scanner + replacement scanner)
    //    var context = await _scanner.ScanAsync(pakDir);

    //    // Merge mods first
    //    foreach (var (pakName, mods) in context.ModsByPak) {
    //        // Create a new PakScanResult for this pak
    //        var result = new PakScanResult {
    //            PakPath = pakName
    //        };

    //        // Add the mods to the read-only Mods collection
    //        result.Mods.AddRange(mods);

    //        // Add any asset replacements for this pak
    //        if (context.ReplacementsByPak.TryGetValue(pakName, out var repl)) {
    //            result.AssetReplacements.AddRange(repl);
    //        }

    //        // Add to the observable collection bound to the UI
    //        ScanResults.Add(result);
    //    }

    //    // Add paks that contain only replacements (no mods)
    //    foreach (var (pakName, repl) in context.ReplacementsByPak) {
    //        // Skip if this pak was already added above
    //        if (ScanResults.Any(r => r.PakPath == pakName))
    //            continue;

    //        var result = new PakScanResult {
    //            PakPath = pakName
    //        };

    //        // Add the asset replacements to the read-only collection
    //        result.AssetReplacements.AddRange(repl);

    //        // Add to the observable collection
    //        ScanResults.Add(result);
    //    }
    //}


    public void ExportJson(string path) {
            ModScanJsonExporter.ExportToFile(ScanResults.ToList(), path);
        }
    }

////using System.Collections.ObjectModel;
////using System.Linq;
////using System.Threading.Tasks;
////using UnchainedLauncher.UnrealModScanner;
////using UnchainedLauncher.UnrealModScanner.Models;
////using UnrealModScanner.Export;


////namespace UnchainedLauncher.GUI.Views.ModScanner {
////    public sealed class ModScanTabVM {
////        private readonly IModScanner _scanner = new UnchainedLauncher.UnrealModScanner.ModScanner();

////        public ObservableCollection<PakScanResult> ScanResults { get; } = new();

////        public async Task ScanAsync(string pakDirectory) {
////            ScanResults.Clear();
////            var results = await _scanner.ScanAsync(pakDirectory);
////            foreach (var r in results)
////                ScanResults.Add(r);
////        }

////        public void ExportJson(string path) {
////            ModScanJsonExporter.ExportToFile(ScanResults.ToList(), path);
////        }
////    }
////}


//using System;
//using System.Collections.ObjectModel;
//using System.Linq;
//using System.Threading;
//using System.Threading.Tasks;
//using UnchainedLauncher.UnrealModScanner;
//using UnrealModScanner.Export;
//using UnrealModScanner.Models;

//namespace UnchainedLauncher.GUI.Views.ModScanner;

//public sealed class ModScanTabVM {
//    private readonly UnchainedLauncher.UnrealModScanner.ModScanner _modScanner;
//    private readonly AssetReplacementScanner _replacementScanner;

//    public ModScanTabVM(
//        UnchainedLauncher.UnrealModScanner.ModScanner modScanner,
//        AssetReplacementScanner replacementScanner) {
//        _modScanner = modScanner;
//        _replacementScanner = replacementScanner;
//    }

//    public ObservableCollection<PakScanResult> ScanResults { get; } = new();

//    public async Task ScanAsync(string pakDirectory, CancellationToken ct = default) {
//        ScanResults.Clear();

//        // 1️⃣ Run mod scanner
//        var modResults = await _modScanner.ScanAsync(pakDirectory, ct);

//        // Index by pak name for fast merge
//        var pakMap = modResults.ToDictionary(
//            p => p.PakPath,
//            StringComparer.OrdinalIgnoreCase);

//        // 2️⃣ Run replacement scanner
//        var replacementResults = await _replacementScanner.ScanAsync(pakDirectory, ct);

//        // 3️⃣ Merge replacements into existing pak entries
//        foreach (var (pakName, replacements) in replacementResults) {
//            if (pakMap.TryGetValue(pakName, out var pak)) {
//                pak.AssetReplacements.AddRange(replacements);
//            }
//            else {
//                // Pak contains ONLY replacements
//                pakMap[pakName] = new PakScanResult {
//                    PakPath = pakName,
//                    AssetReplacements = replacements
//                };
//            }
//        }

//        // 4️⃣ Publish to UI
//        foreach (var pak in pakMap.Values.OrderBy(p => p.PakPath))
//            ScanResults.Add(pak);
//    }

//    public void ExportJson(string path) {
//        ModScanJsonExporter.ExportToFile(ScanResults.ToList(), path);
//    }
//}
