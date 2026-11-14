using log4net;
using Newtonsoft.Json;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.GUI.ViewModels;
using UnchainedLauncher.UnrealModScanner.GUI.ViewModels;
using UnchainedLauncher.UnrealModScanner.JsonModels;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnchainedLauncher.UnrealModScanner.Services;

namespace UnchainedLauncher.GUI.Services;

[AddINotifyPropertyChangedInterface]
public class AvailableModsAndMapsService : IDisposable {
    private static readonly ILog Logger = LogManager.GetLogger(nameof(AvailableModsAndMapsService));

    public ObservableCollection<MapDto> AvailableMaps { get; }
    public ObservableCollection<BlueprintDto> AvailableServerModBlueprints { get; }
    public ObservableCollection<ServerModActorVM> AvailableServerModActors { get; }
    public LocalModScanVM LocalModScan { get; }
    public event EventHandler? ModStateChanged;

    private PakDirManifest? _cachedScanManifest = null;
    private readonly IModManager _modManager;
    private readonly IUnmanagedPakSettings? _settings;
    private readonly List<MapDto> _defaultMaps;
    private readonly Dictionary<string, ServerModActorVM> _serverActorsBySource = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _validationLock = new(1, 1);
    private int _refreshVersion;
    private Task _pendingRefresh = Task.CompletedTask;
    private readonly Task _initialScan;
    private readonly ModScanTabVM _modScanTab;
    private FileSystemWatcher? _pakWatcher;
    private DispatcherTimer? _pakRefreshTimer;
    private Task _localRefresh = Task.CompletedTask;
    private bool _localRefreshRequested;
    private bool _disposed;
    private readonly HashSet<string> _deletedLocalBlueprints = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PakManifest> _observedLocalPaks = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _unchangedLocalBlueprints = new(StringComparer.OrdinalIgnoreCase);

    public void StartWatchingLocalPaks() {
        var directory = _modScanTab.DefaultPakDirectory;
        if (_disposed || _pakWatcher != null || directory == null || !Directory.Exists(directory)) return;
        var dispatcher = Dispatcher.CurrentDispatcher;
        _pakRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _pakRefreshTimer.Tick += async (_, _) => {
            _pakRefreshTimer.Stop();
            await RefreshLocalPaksAsync();
        };
        void QueueRefresh() => dispatcher.InvokeAsync(() => {
            if (_disposed) return;
            _pakRefreshTimer.Stop();
            _pakRefreshTimer.Start();
        });
        try {
            _pakWatcher = new FileSystemWatcher(directory, "*.pak") {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            _pakWatcher.Created += (_, _) => QueueRefresh();
            _pakWatcher.Deleted += (_, _) => QueueRefresh();
            _pakWatcher.Changed += (_, _) => QueueRefresh();
            _pakWatcher.Renamed += (_, _) => QueueRefresh();
            _pakWatcher.Error += (_, _) => QueueRefresh();
            _pakWatcher.EnableRaisingEvents = true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
            _pakWatcher?.Dispose();
            _pakWatcher = null;
            Logger.Warn("Could not watch the local pak directory.", error);
        }
    }

    public Task RefreshLocalPaksAsync() {
        if (_disposed) return Task.CompletedTask;
        _localRefreshRequested = true;
        if (_localRefresh.IsCompleted) _localRefresh = RefreshLocalPaksCoreAsync();
        return _localRefresh;
    }

    private async Task RefreshLocalPaksCoreAsync() {
        do {
            _localRefreshRequested = false;
            try {
                await _initialScan;
                await _modScanTab.WaitForScanAsync();
                var previousPaks = _observedLocalPaks.Values.ToArray();
                var deletedActors = await Task.Run(() => previousPaks
                    .Where(pak => !string.IsNullOrWhiteSpace(pak.PakPath) && !File.Exists(pak.PakPath))
                    .SelectMany(pak => pak.Inventory.RelevantBlueprints()).Select(ServerBlueprintIdentity.Key).ToArray());
                await _modScanTab.RefreshCachedAsync();
                await LocalModScan.RefreshAsync();
                await RefreshCatalogAsync();
                _deletedLocalBlueprints.UnionWith(deletedActors);
                ModStateChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception error) {
                Logger.Warn("Could not refresh collections after a local pak change.", error);
            }
        } while (_localRefreshRequested && !_disposed);
    }

    public void Dispose() {
        _disposed = true;
        _pakWatcher?.Dispose();
        _pakRefreshTimer?.Stop();
    }

    public AvailableModsAndMapsService(IModManager modManager, ModScanTabVM modScanTab, IUnmanagedPakSettings? settings = null) {
        _modManager = modManager;
        _modScanTab = modScanTab;
        _settings = settings;
        LocalModScan = new LocalModScanVM(modManager, modScanTab);
        _defaultMaps = GetDefaultMaps().ToList();
        AvailableMaps = new ObservableCollection<MapDto>();
        AvailableServerModBlueprints = new ObservableCollection<BlueprintDto>();
        AvailableServerModActors = new ObservableCollection<ServerModActorVM>();

        modManager.ModDisabled += _ => OnModStateChanged();
        modManager.ModEnabled += (_, _) => OnModStateChanged();
        WeakEventManager<IModManager, EventArgs>.AddHandler(modManager, nameof(IModManager.MetadataRefreshed), OnMetadataRefreshed);
        if (settings is INotifyPropertyChanged notifyingSettings)
            PropertyChangedEventManager.AddHandler(notifyingSettings, (_, _) => OnModStateChanged(), nameof(IUnmanagedPakSettings.AutomaticallyDownloadPakDependencies));

        HandleLatestScanUpdate(modScanTab.LastScanResult);
        modScanTab.PropertyChanged += (_, args) => {
            if (args.PropertyName == nameof(modScanTab.ResultsVisual))
                HandleLatestScanUpdate(modScanTab.LastScanResult);
        };
        _initialScan = LoadCachedScanAsync(modScanTab);
    }

    private static async Task LoadCachedScanAsync(ModScanTabVM tab) {
        try { await tab.LoadCachedAsync(); }
        catch (Exception error) { Logger.Warn($"Could not load cached pak scan: {error.Message}"); }
    }

    // UpdateModsList resumes on its caller's UI context, just like enable/disable and scan updates.
    private void OnMetadataRefreshed(object? sender, EventArgs args) {
        _ = LocalModScan.RefreshAsync();
        _ = RequestRefresh();
    }

    private async void OnModStateChanged() {
        _ = LocalModScan.RefreshAsync();
        await RequestRefresh();
        await WaitForCatalogAsync();
        ModStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ReconcileCatalog(IReadOnlyDictionary<string, string?>? hashes = null) {
        var releases = _modManager.GetEnabledAndDependencyReleases(includeDependencies: _settings?.AutomaticallyDownloadPakDependencies ?? true).ToList();
        var inactivePaths = _modManager.Mods.SelectMany(mod => mod.Releases)
            .SelectMany(release => release.Manifest.RelevantBlueprints())
            .Select(ServerBlueprintIdentity.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        inactivePaths.ExceptWith(releases.SelectMany(release => release.Manifest.RelevantBlueprints())
            .Select(ServerBlueprintIdentity.Key));
        var scannedPaks = _cachedScanManifest?.Paks.Where(pak => !string.IsNullOrWhiteSpace(pak.PakPath)).ToList() ?? [];
        var scannedPaths = scannedPaks.Select(pak => Path.GetFullPath(pak.PakPath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string? CurrentHash(string path) => hashes != null && hashes.TryGetValue(Path.GetFullPath(path), out var hash) ? hash : null;
        // Unknown ownership stays conservative; known ownership must match the bytes on disk.
        // Registry-only catalogs still do not read the pak inventory or files.
        var managedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var replacedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (scannedPaks.Count > 0) {
            foreach (var pak in _modManager.PakDir.ManagedPaks) {
                var path = Path.GetFullPath(Path.Combine(_modManager.PakDir.DirPath, pak.PakFileName));
                if (!scannedPaths.Contains(path)) continue;
                var release = _modManager.GetRelease(pak.Coordinates).FirstOrDefault();
                var hash = CurrentHash(path);
                if (release != null && !string.IsNullOrWhiteSpace(release.ReleaseHash) && hash != null
                    && !string.Equals(hash, release.ReleaseHash, StringComparison.OrdinalIgnoreCase))
                    replacedPaths.Add(path);
                else
                    managedPaths.Add(path);
            }
        }
        var manifests = releases.Select(release => (PakPath: _modManager.PakDir.GetManagedPakFilePath(ReleaseCoordinates.FromRelease(release))
                .IfNone(() => Path.GetFullPath(Path.Combine(_modManager.PakDir.DirPath ?? "", release.PakFileName))), Inventory: release.Manifest,
                release.ReleaseHash, Source: release.Info.Name))
            .Where(pak => !scannedPaths.Contains(Path.GetFullPath(pak.PakPath))
                || string.Equals(CurrentHash(pak.PakPath), pak.ReleaseHash, StringComparison.OrdinalIgnoreCase))
            .Select(pak => (pak.PakPath, pak.Inventory, pak.Source))
            .Concat(scannedPaks.Where(pak => !managedPaths.Contains(Path.GetFullPath(pak.PakPath))
                && !string.IsNullOrWhiteSpace(pak.PakHash)
                && string.Equals(CurrentHash(pak.PakPath), pak.PakHash, StringComparison.OrdinalIgnoreCase))
                .Select(pak => (pak.PakPath, Inventory: pak.Inventory with {
                    Blueprints = pak.Inventory.Blueprints.Where(actor => replacedPaths.Contains(Path.GetFullPath(pak.PakPath))
                        || !inactivePaths.Contains(ServerBlueprintIdentity.Key(actor))).ToList()
                }, Source: Path.GetFileName(pak.PakPath)))).ToList();
        Reconcile(AvailableMaps, _defaultMaps.Concat(manifests.SelectMany(pak => pak.Inventory.RelevantMaps())));
        // Catalog identity is actor path plus physical pak path, not blueprint metadata.
        var actors = manifests.SelectMany(pak => pak.Inventory.RelevantBlueprints().Select(actor => (
            Key: Path.GetFullPath(pak.PakPath) + "\0" + actor.Path.Replace('\\', '/'), Actor: actor, pak.Source)))
            .DistinctBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(entry => entry.Key, entry => (entry.Actor, entry.Source), StringComparer.OrdinalIgnoreCase);
        foreach (var stale in _serverActorsBySource.Where(entry => !actors.TryGetValue(entry.Key, out var actor) || actor.Actor != entry.Value.Blueprint).ToList()) {
            var index = AvailableServerModBlueprints.ToList().FindIndex(actor => ReferenceEquals(actor, stale.Value.Blueprint));
            if (index >= 0) AvailableServerModBlueprints.RemoveAt(index);
            AvailableServerModActors.Remove(stale.Value);
            _serverActorsBySource.Remove(stale.Key);
        }
        foreach (var (key, actor) in actors) {
            if (_serverActorsBySource.TryGetValue(key, out var existing)) existing.Source = actor.Source;
            else {
                var entry = new ServerModActorVM(actor.Actor, actor.Source);
                _serverActorsBySource.Add(key, entry);
                AvailableServerModBlueprints.Add(actor.Actor);
                AvailableServerModActors.Add(entry);
            }
        }
    }

    // External pak changes do not raise mod-manager events.
    public async Task RefreshCatalogAsync() {
        // A scan or toggle during validation must not let launch use an older snapshot.
        await RequestRefresh();
        await WaitForCatalogAsync();
    }

    public async Task WaitForCatalogAsync() {
        await _initialScan;
        Task refresh;
        do {
            await _modScanTab.WaitForScanAsync();
            refresh = _pendingRefresh;
            await refresh;
        } while (refresh != _pendingRefresh || _modScanTab.IsScanning);
    }

    private Task RequestRefresh() {
        var version = ++_refreshVersion;
        var paths = _observedLocalPaks.Keys.ToArray();
        // Registry-only availability stays synchronous, even if old local selection evidence remains.
        if (_cachedScanManifest?.Paks.Any(pak => !string.IsNullOrWhiteSpace(pak.PakPath)) != true) {
            ReconcileCatalog();
            if (paths.Length == 0) return _pendingRefresh = Task.CompletedTask;
        }
        return _pendingRefresh = ValidateAndReconcileAsync(paths, version);
    }

    private async Task ValidateAndReconcileAsync(string[] paths, int version) {
        var hashes = await Task.Run(async () => {
            await _validationLock.WaitAsync();
            try {
                if (version != Volatile.Read(ref _refreshVersion)) return null;
                return await PakScanCache.GetHashesAsync(paths);
            }
            finally { _validationLock.Release(); }
        });
        // Resume on the caller's UI context; obsolete work never changes bound collections.
        if (version == _refreshVersion && hashes != null) {
            // Retain selection evidence, not availability, while dependency-invalid scans await revalidation.
            _unchangedLocalBlueprints.Clear();
            _unchangedLocalBlueprints.UnionWith(_observedLocalPaks.Where(entry =>
                    !string.IsNullOrWhiteSpace(entry.Value.PakHash)
                    && hashes.TryGetValue(entry.Key, out var hash)
                    && string.Equals(hash, entry.Value.PakHash, StringComparison.OrdinalIgnoreCase))
                .SelectMany(entry => entry.Value.Inventory.RelevantBlueprints()).Select(ServerBlueprintIdentity.Key));
            ReconcileCatalog(hashes);
        }
    }

    public BlueprintDto[] GetConfirmedInactiveSelections(IEnumerable<BlueprintDto> selections) {
        var deletedPaths = _deletedLocalBlueprints.Except(AvailableServerModBlueprints.Select(ServerBlueprintIdentity.Key),
            StringComparer.OrdinalIgnoreCase).Except(_unchangedLocalBlueprints, StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Incomplete dependency metadata is not evidence of disablement.
        if (_modManager.EnabledModReleaseCoordinates.Any(coordinates => _modManager.GetRelease(coordinates).IsNone)
            || _modManager.GetEnabledAndDependencyReleases(includeDependencies: _settings?.AutomaticallyDownloadPakDependencies ?? true)
                .SelectMany(release => _modManager.GetLatestRelease(ReleaseCoordinates.FromRelease(release)).ToList())
                .SelectMany(release => release.Info.Dependencies)
                .Any(dependency => _modManager.GetLatestRelease(ModIdentifier.FromDependency(dependency)).IsNone))
            return selections.Where(actor => deletedPaths.Contains(ServerBlueprintIdentity.Key(actor))).ToArray();

        var inactivePaths = _modManager.Mods.SelectMany(mod => mod.Releases)
            .SelectMany(release => release.Manifest.RelevantBlueprints())
            .Select(ServerBlueprintIdentity.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        inactivePaths.ExceptWith(AvailableServerModBlueprints.Select(ServerBlueprintIdentity.Key));
        inactivePaths.UnionWith(deletedPaths);
        return selections.Where(actor => inactivePaths.Contains(ServerBlueprintIdentity.Key(actor))).ToArray();
    }

    private static void Reconcile<T>(ObservableCollection<T> catalog, IEnumerable<T> assets) {
        var desired = assets.Distinct().ToList();
        // Keep surviving entries in place so selection and rotation bindings are not reset.
        foreach (var stale in catalog.Where(asset => !desired.Contains(asset)).ToList())
            catalog.Remove(stale);
        foreach (var asset in desired.Where(asset => !catalog.Contains(asset)))
            catalog.Add(asset);
    }

    private static IEnumerable<MapDto> GetDefaultMaps() {
        try {
            using var defaultMapsListStream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("UnchainedLauncher.GUI.Resources.DefaultMaps.json");
            if (defaultMapsListStream != null) {
                using var reader = new StreamReader(defaultMapsListStream);

                var defaultMapsString = reader.ReadToEnd();
                var defaultMaps = JsonConvert.DeserializeObject<List<MapDto>>(defaultMapsString);
                if (defaultMaps != null)
                    return defaultMaps;
                else
                    Logger.Warn("Failed to deserialize default maps. Vanilla Maps list will be empty.");
            }
        }
        catch (Exception e) {
            Logger.Warn("Failed to deserialize default maps. Vanilla Maps list will be empty.", e);
        }

        return [];
    }

    private void HandleLatestScanUpdate(ModScanResult? scanResult) {
        Logger.Debug("Processing latest scan...");

        _cachedScanManifest = scanResult == null ? null : ModManifestConverter.ProcessModScan(scanResult);
        // Scan publication can precede the watcher refresh; do not lose physical source evidence.
        foreach (var pak in _cachedScanManifest?.Paks ?? []) {
            if (!string.IsNullOrWhiteSpace(pak.PakPath)) _observedLocalPaks[Path.GetFullPath(pak.PakPath)] = pak;
        }
        _ = RequestRefresh();
    }
}