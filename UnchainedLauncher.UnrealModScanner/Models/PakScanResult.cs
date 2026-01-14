


using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using UnchainedLauncher.UnrealModScanner.Models;
using UnchainedLauncher.UnrealModScanner.Models.Chivalry2;
using UnchainedLauncher.UnrealModScanner.Models.Chivalry2.UnchainedLauncher.UnrealModScanner.Models;
using UnchainedLauncher.UnrealModScanner.Models.Dto;
using UnchainedLauncher.UnrealModScanner.ViewModels.Nodes;

namespace UnrealModScanner.Models;

//[JsonObject(MemberSerialization.OptOut)]
/// <summary>
/// Class containing the results of ModScanOrchestrator. 
/// Used in UI visualization and JSON export
/// <div/>
/// TODO: Split this off so it only contains the data retrieved during scan.
/// <div/>
/// TODO: Implement <see cref="PakScanResultVM"/>
/// </summary>
public sealed class PakScanResult {
    public PakScanResult(bool isExpanded = true) {
        IsExpanded = isExpanded;
    }
    /// <summary>
    /// Pak Path relative to scan directory
    /// </summary>
    [JsonProperty("pak_path")]
    public string PakPath { get; set; } = string.Empty;
    /// <summary>
    /// SHA-512 hash (by default) of current pak
    /// </summary>
    [JsonProperty("pak_hash")]
    public string? PakHash { get; set; } = string.Empty;
    /// <summary>
    /// Assets returned by GenericCDOProcessor and ReferenceDiscoveryProcessor
    /// </summary>
    [JsonProperty("extracted_assets")]
    public ConcurrentDictionary<string, ConcurrentBag<GenericAssetEntry>> GenericEntries { get; } = new();
    /// <summary>
    /// Assets discovered by ReplacementProcessor
    /// </summary>
    [JsonProperty("asset_replacements")]
    public ConcurrentBag<AssetReplacementInfo> _AssetReplacements { get; } = new();
    /// <summary>
    /// Levels extracted with MapProcessor
    /// </summary>
    [JsonProperty("maps")]
    public ConcurrentBag<GameMapInfo> _Maps { get; } = new();
    /// <summary>
    /// Uncategorized assets in pak (Not in official directories)
    /// </summary>
    [JsonProperty("arbitrary_assets")]
    public ConcurrentBag<ArbitraryAssetInfo> ArbitraryAssets { get; } = new();
    /// <summary>
    /// Scan result from AssetRegistry scan
    /// </summary>
    [JsonProperty("internal_assets")]
    public ConcurrentBag<GameInternalAssetInfo> InternalAssets { get; } = new();
    
    public void AddGenericEntry(GenericAssetEntry entry, string base_name) {
        var bag = GenericEntries.GetOrAdd(base_name, _ => new ConcurrentBag<GenericAssetEntry>());
        bag.Add(entry);
    }
    
    // Chivalry 2 specific
    
    [JsonProperty("markers")]
    public ConcurrentBag<ModMarkerInfo> _Markers { get; } = new();
    
    // UI 
    
    [JsonIgnore]
    public ObservableCollection<PakChildNode> Children { get; } = new();
    
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
    
    // Serialization filters
    public bool ShouldSerializeInternalAssets() { return InternalAssets.Count > 0; }
    public bool ShouldSerializeArbitraryAssets() { return ArbitraryAssets.Count > 0; }
    public bool ShouldSerialize_Maps() { return _Maps.Count > 0; }
    public bool ShouldSerialize_AssetReplacements() { return _AssetReplacements.Count > 0; }
    public bool ShouldSerialize_Markers() { return _Markers.Count > 0; }
    public bool ShouldSerializeGenericEntries() { return GenericEntries.Count > 0; }
}