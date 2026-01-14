using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using UnchainedLauncher.UnrealModScanner.GUI.ViewModels.Nodes;
using UnchainedLauncher.UnrealModScanner.Models.Chivalry2.UnchainedLauncher.UnrealModScanner.Models;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnchainedLauncher.UnrealModScanner.ViewModels.Nodes;
using UnrealModScanner.Models;

// TODO: implemen. is this a viewmodel?
namespace UnchainedLauncher.UnrealModScanner.ViewModels {
    public class PakScanResultVM {
        public PakScanResultVM() {
            Results = new ObservableCollection<PakScanResult>();
            Children = new ObservableCollection<PakChildNode>(); 
        }
        public PakScanResultVM( ModScanResult scanResults) {
            Results = new ObservableCollection<PakScanResult>();
            Children = InitChildren(scanResults); 
        }

        private ObservableCollection<PakChildNode> InitChildren(ModScanResult scanResults) {
            Application.Current.Dispatcher.Invoke(() => Results.Clear());
            var children = new ObservableCollection<PakChildNode>();
            
            foreach (var (pakName, scanResult) in scanResults.Paks) {
                int numMods = scanResult._Markers.Sum(m => m.Blueprints.Count);
                int numReplacements = scanResult._AssetReplacements.Count;
                int numMaps = scanResult._Maps.Count;
                int numOther = scanResult.ArbitraryAssets.Count;

                // Generate Summary Text
                var parts = new List<string>();
                if (numMods > 0) parts.Add($"{numMods} mods");
                if (numReplacements > 0) parts.Add($"{numReplacements} assets");
                if (numMaps > 0) parts.Add($"{numMaps} maps");

                var summary = string.Join(", ", parts);
                scanResult.PakPathExpanded = $"📦 {scanResult.PakPath}" + (summary.Length > 0 ? $" ({summary})" : "");
                scanResult.IsExpanded = false;
                
                var wrapper = new PakResultWrapperNode(summary, false, scanResult.PakPath, scanResult);
                wrapper.PakPath = scanResult.PakPath;
                wrapper.PakPathCollapsed = $"📦 {scanResult.PakPath}";
                wrapper.PakPathExpanded = $"📦 {scanResult.PakPath}" + (summary.Length > 0 ? $" ({summary})" : "");

                // --- Group: Mods ---
                if (numMods > 0) {
                    var modsGroup = new PakGroupNode($"Mods ({numMods})");
                    foreach (var marker in scanResult._Markers)
                        foreach (var mod in marker.Blueprints)
                            modsGroup.Children.Add(new ModTreeNode(mod));

                    wrapper.Children.Add(modsGroup);
                }

                // --- Group: Asset Replacements ---
                if (numReplacements > 0) {
                    var replGroup = new PakGroupNode($"Asset Replacements ({numReplacements})", false);
                    foreach (var repl in scanResult._AssetReplacements)
                        replGroup.Children.Add(new AssetReplacementTreeNode(repl));

                    wrapper.Children.Add(replGroup);
                }

                // --- Group: Maps ---
                if (numMaps > 0) {
                    var mapsGroup = new PakGroupNode($"Maps ({numMaps})");
                    foreach (var map in scanResult._Maps) {
                        mapsGroup.Children.Add(new AssetReplacementTreeNode(new AssetReplacementInfo {
                            AssetPath = map.AssetPath,
                            AssetHash = map.AssetHash,
                            ClassName = map.GameMode,
                            Extension = "umap"
                        }));
                    }
                    wrapper.Children.Add(mapsGroup);
                }

                // --- Group: Other (Arbitrary) ---
                if (numOther > 0) {
                    var otherGroup = new PakGroupNode($"Other ({numOther})");
                    foreach (var arb in scanResult.ArbitraryAssets) {
                        otherGroup.Children.Add(new AssetReplacementTreeNode(new AssetReplacementInfo {
                            AssetPath = arb.AssetPath,
                            AssetHash = arb.AssetHash,
                            ClassName = arb.ClassName
                        }));
                    }
                    wrapper.Children.Add(otherGroup);
                }

                children.Add(wrapper);
                // 6. Push to UI Collection
                Application.Current.Dispatcher.Invoke(() => Results.Add(scanResult));
            }
            
            return children;
        }

        public void SetChildrenExpanded(bool newState = true) {
            foreach (var res in Results) {
                res.IsExpanded = false;
            }
        }
        
        [JsonIgnore]
        public ObservableCollection<PakChildNode> Children { get; init; } = new();
        
        public ObservableCollection<PakScanResult> Results { get; } = new();
    
        private bool _isChecked;
        /// <summary>
        /// GUI View state
        /// </summary>
        private bool _isExpanded;
        /// <summary>
        /// Whether this category is expanded
        /// </summary>
        [JsonIgnore]
        public string PakPathExpanded { get; set; }
    
        [JsonIgnore]
        public bool IsExpanded {
            get => _isExpanded;
            set {
                if (_isExpanded != value) {
                    _isExpanded = value;
                    if (_isExpanded && Children.Count == 1) {
                        Children.First().IsExpanded = true;
                    }
                    OnPropertyChanged();
                }
            }
        }
    
        [JsonIgnore]
        public bool IsChecked {
            get => _isChecked;
            set {
                if (_isChecked != value) {
                    _isChecked = value;
                    OnPropertyChanged();
                }
            }
        }
    
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}