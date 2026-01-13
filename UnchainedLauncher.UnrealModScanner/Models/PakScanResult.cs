


using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using UnchainedLauncher.UnrealModScanner.Models;
using UnchainedLauncher.UnrealModScanner.Models.Dto;
using UnchainedLauncher.UnrealModScanner.Models.UnchainedLauncher.UnrealModScanner.Models;
using UnchainedLauncher.UnrealModScanner.ViewModels.Nodes;

namespace UnrealModScanner.Models;

//[JsonObject(MemberSerialization.OptOut)]
public sealed class PakScanResult {

    public PakScanResult(bool isExpanded = true) {
        IsExpanded = isExpanded;
    }

    private bool _isChecked;

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
    public void AddGenericEntry(GenericAssetEntry entry, string base_name) {
        var bag = GenericEntries.GetOrAdd(base_name, _ => new ConcurrentBag<GenericAssetEntry>());
        bag.Add(entry);
    }

    private bool _isExpanded;

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
    [JsonProperty("pak_path")]
    public string PakPath { get; set; } = string.Empty;
    [JsonIgnore]
    public string PakPathExpanded { get; set; }
    [JsonProperty("pak_hash")]
    public string? PakHash { get; set; } = string.Empty;

    [JsonProperty("extracted_assets")]
    public ConcurrentDictionary<string, ConcurrentBag<GenericAssetEntry>> GenericEntries { get; } = new();

    [JsonIgnore]
    public ObservableCollection<PakChildNode> Children { get; } = new();

    [JsonProperty("markers")]
    public ConcurrentBag<ModMarkerInfo> _Markers { get; } = new();
    [JsonProperty("asset_replacements")]
    public ConcurrentBag<AssetReplacementInfo> _AssetReplacements { get; } = new();
    [JsonProperty("maps")]
    public ConcurrentBag<GameMapInfo> _Maps { get; } = new();
    [JsonProperty("arbitrary_assets")]
    public ConcurrentBag<ArbitraryAssetInfo> ArbitraryAssets { get; } = new();
    [JsonProperty("internal_assets")]
    public ConcurrentBag<GameInternalAssetInfo> InternalAssets { get; } = new();
    public bool ShouldSerializeInternalAssets() { return InternalAssets.Count > 0; }
    public bool ShouldSerializeArbitraryAssets() { return ArbitraryAssets.Count > 0; }
    public bool ShouldSerialize_Maps() { return _Maps.Count > 0; }
    public bool ShouldSerialize_AssetReplacements() { return _AssetReplacements.Count > 0; }
    public bool ShouldSerialize_Markers() { return _Markers.Count > 0; }
    public bool ShouldSerializeGenericEntries() { return GenericEntries.Count > 0; }
}