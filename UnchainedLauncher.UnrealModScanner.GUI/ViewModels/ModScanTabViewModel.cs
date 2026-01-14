using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using UnchainedLauncher.UnrealModScanner.ViewModels;
using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.GUI.ViewModels;

public class ModScanTabViewModel : INotifyPropertyChanged {
    // This is what your TreeView ItemsSource="{Binding ModScanTabVM.ScanResults}" looks for
    private ObservableCollection<PakScanResult> _scanResults = new();
    public PakScanResultVM ResultsVisual { get; set; } = new();
    public ModScanTabVM ModScanTab { get; set; } = new();
    
    public ObservableCollection<PakScanResult> ScanResults {
        get => _scanResults;
        set { _scanResults = value; OnPropertyChanged(); }
    }

    // Example Command Logic
    public void ExecuteScan() {
        // 1. Clear old results
        ScanResults.Clear();

        // 2. Call your Orchestrator here
        // 3. Populate ScanResults with the new PakScanResult objects
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}