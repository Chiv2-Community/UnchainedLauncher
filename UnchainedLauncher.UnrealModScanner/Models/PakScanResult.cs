


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

    public ObservableCollection<PakChildNode> Children { get; } = new();

    public ConcurrentBag<ModMarkerInfo> _Markers { get; } = new();
    public ConcurrentBag<AssetReplacementInfo> _AssetReplacements { get; } = new();
    public ConcurrentBag<GameMapInfo> _Maps { get; } = new();
    public ConcurrentBag<ArbitraryAssetInfo> ArbitraryAssets { get; } = new();
    public ConcurrentBag<GameInternalAssetInfo> InternalAssets { get; } = new();
}