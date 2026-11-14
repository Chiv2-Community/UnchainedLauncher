using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.UnrealModScanner.Config;
using UnchainedLauncher.UnrealModScanner.Config.Games;
using UnchainedLauncher.UnrealModScanner.Export;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnchainedLauncher.UnrealModScanner.Services;
using UnchainedLauncher.UnrealModScanner.ViewModels;

namespace UnchainedLauncher.UnrealModScanner.GUI.ViewModels;

public partial class ModScanTabVM : ObservableObject {
    [ObservableProperty]
    private PakScanResultVM _resultsVisual = new();
    public ModScanResult? LastScanResult { get; private set; }
    public ScanOptions? LoadedConfig { get; set; }
    public string? DefaultPakDirectory { get; set; }

    [ObservableProperty]
    private double _scanProgress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotScanning))]
    private bool _isScanning;
    public bool IsNotScanning => !IsScanning;
    private Task scanTask = Task.CompletedTask;

    public Task ScanAsync(string pakDir) => StartScan(pakDir, cachedOnly: false);

    public Task LoadCachedAsync() {
        var directory = DefaultPakDirectory;
        return directory != null && Directory.Exists(directory) ? StartScan(directory, cachedOnly: true) : Task.CompletedTask;
    }

    public Task WaitForScanAsync() => IsScanning ? scanTask : Task.CompletedTask;

    private Task StartScan(string pakDir, bool cachedOnly) => IsScanning ? scanTask : scanTask = ScanCoreAsync(pakDir, cachedOnly);

    private async Task ScanCoreAsync(string pakDir, bool cachedOnly) {
        IsScanning = true;
        ScanProgress = 0;
        try {
            var progress = new Progress<double>(percent => ScanProgress = percent);
            var result = await PakScanCache.ScanAsync(pakDir, LoadedConfig ?? GameScanOptions.Chivalry2, progress, cachedOnly);
            LastScanResult = result;
            ResultsVisual = new PakScanResultVM(result);
        }
        finally { IsScanning = false; }
    }

    public void ExportJson(string path, bool createManifest) {
        if (LastScanResult == null) return;
        if (createManifest) ModScanJsonExporter.ExportToFile(ModManifestConverter.ProcessModScan(LastScanResult), path);
        else ModScanJsonExporter.ExportToFile(LastScanResult, path);
    }
}