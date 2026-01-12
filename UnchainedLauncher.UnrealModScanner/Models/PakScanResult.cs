


using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using UnchainedLauncher.UnrealModScanner.Models;
using UnchainedLauncher.UnrealModScanner.Models.UnchainedLauncher.UnrealModScanner.Models;
using UnchainedLauncher.UnrealModScanner.ViewModels.Nodes;

namespace UnrealModScanner.Models;


public sealed class PakScanResult {

    public PakScanResult(bool isExpanded = true) {
        IsExpanded = isExpanded;
    }

    private bool _isChecked;

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

    private bool _isExpanded;
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
    public required string PakPath { get; init; }
    public string PakPathExpanded { get; set; }
    public string? PakHash { get; set; }

    //public List<BlueprintModInfo> Mods { get; } = new();
    //public List<ModMarkerInfo> Markers { get; } = new();
    //public List<GameMapInfo> Maps { get; } = new();
    //public List<AssetReplacementInfo> AssetReplacements { get; } = new();
    public ObservableCollection<PakChildNode> Children { get; } = new();

    public ConcurrentBag<ModMarkerInfo> _Markers { get; } = new();
    public ConcurrentBag<AssetReplacementInfo> _AssetReplacements { get; } = new();
    public ConcurrentBag<GameMapInfo> _Maps { get; } = new();
    public ConcurrentBag<ArbitraryAssetInfo> ArbitraryAssets { get; } = new();
    public ConcurrentBag<GameInternalAssetInfo> InternalAssets { get; } = new();


    //public void MergeFrom(PakScanResult other) {
    //    Mods.AddRange(other.Mods);
    //    Markers.AddRange(other.Markers);
    //    Maps.AddRange(other.Maps);
    //    AssetReplacements.AddRange(other.AssetReplacements);

    //    foreach (var child in other.Children)
    //        Children.Add(child);


    //    // Fill missing scalar values
    //    PakHash ??= other.PakHash;
    //}

    //public static Dictionary<string, PakScanResult> MergeAll(
    //params Dictionary<string, PakScanResult>[] dictionaries) {
    //    var result = new Dictionary<string, PakScanResult>();

    //    foreach (var dict in dictionaries) {
    //        foreach (var (key, value) in dict) {
    //            if (!result.TryGetValue(key, out var existing))
    //                result[key] = value;
    //            else
    //                existing.MergeFrom(value);
    //        }
    //    }

    //    return result;
    //}

}
// TreeView children (ONE collection!)
//    public IReadOnlyList<ITreeNode> Children =>
//        Mods.Select(m => new ModTreeNode(m))
//            .Cast<ITreeNode>()
//            .Concat(AssetReplacements.Select(a => new AssetReplacementTreeNode(a)))
//            .ToList();
//}

