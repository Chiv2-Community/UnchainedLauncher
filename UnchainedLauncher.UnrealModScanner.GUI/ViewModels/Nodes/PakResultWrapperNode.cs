using UnchainedLauncher.UnrealModScanner.GUI.ViewModels.Nodes;
using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.GUI.ViewModels.Nodes;
public class PakResultWrapperNode : PakGroupNode {
    public PakScanResult Data { get; }
    // Make sure this is a Property for Binding
    public string PakPath { get; set; } 
    public string PakPathExpanded { get; set; }
    public string PakPathCollapsed { get; set; }

    public PakResultWrapperNode(string title, bool isExpanded, string pakPath, PakScanResult data) 
        : base(title, isExpanded) {
        Data = data;
        PakPath = pakPath;
        IsExpanded = isExpanded;

        foreach(var child in data.Children) {
            this.Children.Add(child);
        }
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
    
}