using CommunityToolkit.Mvvm.ComponentModel;
using log4net;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.UnrealModScanner.Config.Games;
using UnchainedLauncher.UnrealModScanner.GUI.ViewModels;
using UnchainedLauncher.UnrealModScanner.Services;

namespace UnchainedLauncher.GUI.ViewModels;

public partial class LocalModScanVM : ObservableObject {
    private static readonly ILog Logger = LogManager.GetLogger(nameof(LocalModScanVM));
    private readonly IModManager manager;
    private Task refresh = Task.CompletedTask;
    private bool refreshRequested;

    public ModScanTabVM Scanner { get; }
    public string PakDirectory => manager.PakDir.DirPath;
    public bool CanScan => HasUnscannedPaks && !Scanner.IsScanning;

    [ObservableProperty]
    private bool hasUnmanagedPaks;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanScan))]
    private bool hasUnscannedPaks;

    [ObservableProperty]
    private int localModActorCount;

    public LocalModScanVM(IModManager manager, ModScanTabVM scanner) {
        this.manager = manager;
        Scanner = scanner;
        scanner.PropertyChanged += (_, args) => {
            if (args.PropertyName != nameof(scanner.IsScanning)) return;
            OnPropertyChanged(nameof(CanScan));
            if (!scanner.IsScanning) _ = RefreshAsync();
        };
    }

    public async Task ScanAsync() {
        if (!CanScan) return;
        try { await Scanner.ScanAsync(PakDirectory); }
        finally { await RefreshAsync(); }
    }

    // Calls originate on the UI context; coalesce filesystem and scan-completion updates.
    public Task RefreshAsync() {
        refreshRequested = true;
        if (refresh.IsCompleted) refresh = RefreshCoreAsync();
        return refresh;
    }

    private async Task RefreshCoreAsync() {
        do {
            refreshRequested = false;
            try {
                if (!Directory.Exists(PakDirectory)) {
                    HasUnmanagedPaks = HasUnscannedPaks = false;
                    LocalModActorCount = 0;
                    continue;
                }
                var options = Scanner.LoadedConfig ?? GameScanOptions.Chivalry2;
                var paths = Directory.GetFiles(PakDirectory, "*.pak")
                    .Where(path => !options.VanillaPakNames.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
                    .Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var inventory = await manager.PakDir.GetManagedPaks();
                var ownership = inventory.Select(pak => (
                    Path: Path.GetFullPath(Path.Combine(PakDirectory, pak.PakFileName)),
                    Release: manager.GetRelease(pak.Coordinates).FirstOrDefault())).ToArray();
                var hashes = await PakScanCache.GetHashesAsync(ownership
                    .Where(pak => paths.Contains(pak.Path)
                        && !string.IsNullOrWhiteSpace(pak.Release?.ReleaseHash)).Select(pak => pak.Path));
                var managed = ownership.Where(pak => {
                    var (path, release) = pak;
                    // Unknown ownership stays conservative; stale known ownership does not hide replacements.
                    return release == null || string.IsNullOrWhiteSpace(release.ReleaseHash)
                        || !hashes.TryGetValue(path, out var hash) || hash == null
                        || string.Equals(hash, release.ReleaseHash, StringComparison.OrdinalIgnoreCase);
                }).Select(pak => pak.Path)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var unmanaged = paths;
                unmanaged.ExceptWith(managed);
                HasUnmanagedPaks = unmanaged.Count > 0;
                if (!HasUnmanagedPaks) {
                    HasUnscannedPaks = false;
                    LocalModActorCount = 0;
                    continue;
                }
                var cached = await PakScanCache.ScanAsync(PakDirectory, options, cachedOnly: true);
                HasUnscannedPaks = unmanaged.Any(path => !cached.Paks.ContainsKey(Path.GetFileName(path)));
                LocalModActorCount = ModManifestConverter.ProcessModScan(cached).Paks
                    .Where(pak => unmanaged.Contains(Path.GetFullPath(pak.PakPath)))
                    .Sum(pak => pak.Inventory.RelevantBlueprints()
                        .DistinctBy(actor => actor.Path.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase).Count());
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
                Logger.Warn("Could not refresh local mod scan status.", error);
                // Keep the action available rather than treating an unreadable pak as scanned.
                HasUnscannedPaks = HasUnmanagedPaks;
            }
        } while (refreshRequested);
    }
}