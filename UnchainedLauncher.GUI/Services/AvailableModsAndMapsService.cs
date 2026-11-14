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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.UnrealModScanner.GUI.ViewModels;
using UnchainedLauncher.UnrealModScanner.JsonModels;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnchainedLauncher.UnrealModScanner.Services;

namespace UnchainedLauncher.GUI.Services;

[AddINotifyPropertyChangedInterface]
public class AvailableModsAndMapsService {
    private static readonly ILog Logger = LogManager.GetLogger(nameof(AvailableModsAndMapsService));

    public ObservableCollection<MapDto> AvailableMaps { get; }
    public ObservableCollection<BlueprintDto> AvailableServerModBlueprints { get; }
    public event EventHandler? ModStateChanged;

    private PakDirManifest? _cachedScanManifest = null;
    private readonly Dictionary<string, (string Hash, long Length, DateTime Modified, string? ScanHash)> _validatedScanFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly IModManager _modManager;
    private readonly IUnmanagedPakSettings? _settings;
    private readonly List<MapDto> _defaultMaps;
    private readonly Dictionary<string, BlueprintDto> _serverBlueprintsBySource = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _validationLock = new(1, 1);
    private int _refreshVersion;
    private bool _forceValidationPending;
    private Task _pendingRefresh = Task.CompletedTask;

    public AvailableModsAndMapsService(IModManager modManager, ModScanTabVM modScanTab, IUnmanagedPakSettings? settings = null) {
        _modManager = modManager;
        _settings = settings;
        _defaultMaps = GetDefaultMaps().ToList();
        AvailableMaps = new ObservableCollection<MapDto>();
        AvailableServerModBlueprints = new ObservableCollection<BlueprintDto>();

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
    }

    // UpdateModsList resumes on its caller's UI context, just like enable/disable and scan updates.
    private void OnMetadataRefreshed(object? sender, EventArgs args) => RequestRefresh();

    private async void OnModStateChanged() {
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
                .IfNone(() => Path.GetFullPath(Path.Combine(_modManager.PakDir.DirPath ?? "", release.PakFileName))), Inventory: release.Manifest, release.ReleaseHash))
            .Where(pak => !scannedPaths.Contains(Path.GetFullPath(pak.PakPath))
                || string.Equals(CurrentHash(pak.PakPath), pak.ReleaseHash, StringComparison.OrdinalIgnoreCase))
            .Select(pak => (pak.PakPath, pak.Inventory))
            .Concat(scannedPaks.Where(pak => !managedPaths.Contains(Path.GetFullPath(pak.PakPath))
                && !string.IsNullOrWhiteSpace(pak.PakHash)
                && string.Equals(CurrentHash(pak.PakPath), pak.PakHash, StringComparison.OrdinalIgnoreCase))
                .Select(pak => (pak.PakPath, Inventory: pak.Inventory with {
                    Blueprints = pak.Inventory.Blueprints.Where(actor => replacedPaths.Contains(Path.GetFullPath(pak.PakPath))
                        || !inactivePaths.Contains(ServerBlueprintIdentity.Key(actor))).ToList()
                }))).ToList();
        Reconcile(AvailableMaps, _defaultMaps.Concat(manifests.SelectMany(pak => pak.Inventory.RelevantMaps())));
        // Catalog identity is actor path plus physical pak path, not blueprint metadata.
        var actors = manifests.SelectMany(pak => pak.Inventory.RelevantBlueprints().Select(actor => (
            Key: Path.GetFullPath(pak.PakPath) + "\0" + actor.Path.Replace('\\', '/'), Actor: actor)))
            .DistinctBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(entry => entry.Key, entry => entry.Actor, StringComparer.OrdinalIgnoreCase);
        foreach (var stale in _serverBlueprintsBySource.Where(entry => !actors.TryGetValue(entry.Key, out var actor) || actor != entry.Value).ToList()) {
            var index = AvailableServerModBlueprints.ToList().FindIndex(actor => ReferenceEquals(actor, stale.Value));
            if (index >= 0) AvailableServerModBlueprints.RemoveAt(index);
            _serverBlueprintsBySource.Remove(stale.Key);
        }
        foreach (var entry in actors.Where(entry => !_serverBlueprintsBySource.ContainsKey(entry.Key))) {
            _serverBlueprintsBySource.Add(entry.Key, entry.Value);
            AvailableServerModBlueprints.Add(entry.Value);
        }
    }

    // External pak changes do not raise mod-manager events.
    public async Task RefreshCatalogAsync() {
        // A scan or toggle during validation must not let launch use an older snapshot.
        await RequestRefresh(verifyHashes: true);
        await WaitForCatalogAsync();
    }

    public async Task WaitForCatalogAsync() {
        Task refresh;
        do {
            refresh = _pendingRefresh;
            await refresh;
        } while (refresh != _pendingRefresh);
    }

    private Task RequestRefresh(bool verifyHashes = false) {
        var version = ++_refreshVersion;
        // Carry a launch's forced validation through superseding scan/toggle requests.
        _forceValidationPending |= verifyHashes;
        var paths = _cachedScanManifest?.Paks.Where(pak => !string.IsNullOrWhiteSpace(pak.PakPath))
            .Select(pak => (Path: Path.GetFullPath(pak.PakPath), ScanHash: pak.PakHash))
            .DistinctBy(pak => pak.Path, StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        // Registry-only catalogs stay synchronous and never touch the disk.
        if (paths.Length == 0) {
            _forceValidationPending = false;
            ReconcileCatalog();
            return _pendingRefresh = Task.CompletedTask;
        }
        return _pendingRefresh = ValidateAndReconcileAsync(paths, version, _forceValidationPending);
    }

    private async Task ValidateAndReconcileAsync((string Path, string? ScanHash)[] paths, int version, bool verifyHashes) {
        var hashes = await Task.Run(async () => {
            await _validationLock.WaitAsync();
            try {
                if (version != Volatile.Read(ref _refreshVersion)) return null;
                return paths.ToDictionary(pak => pak.Path, pak => GetCurrentHash(pak.Path, pak.ScanHash, verifyHashes), StringComparer.OrdinalIgnoreCase);
            }
            finally { _validationLock.Release(); }
        });
        // Resume on the caller's UI context; obsolete work never changes bound collections.
        if (version == _refreshVersion && hashes != null) {
            _forceValidationPending = false;
            ReconcileCatalog(hashes);
        }
    }

    public BlueprintDto[] GetConfirmedInactiveSelections(IEnumerable<BlueprintDto> selections) {
        // Incomplete dependency metadata is not evidence of disablement.
        if (_modManager.EnabledModReleaseCoordinates.Any(coordinates => _modManager.GetRelease(coordinates).IsNone)
            || _modManager.GetEnabledAndDependencyReleases(includeDependencies: _settings?.AutomaticallyDownloadPakDependencies ?? true)
                .SelectMany(release => _modManager.GetLatestRelease(ReleaseCoordinates.FromRelease(release)).ToList())
                .SelectMany(release => release.Info.Dependencies)
                .Any(dependency => _modManager.GetLatestRelease(ModIdentifier.FromDependency(dependency)).IsNone)) return [];

        var inactivePaths = _modManager.Mods.SelectMany(mod => mod.Releases)
            .SelectMany(release => release.Manifest.RelevantBlueprints())
            .Select(ServerBlueprintIdentity.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        inactivePaths.ExceptWith(AvailableServerModBlueprints.Select(ServerBlueprintIdentity.Key));
        return selections.Where(actor => inactivePaths.Contains(ServerBlueprintIdentity.Key(actor))).ToArray();
    }

    private string? GetCurrentHash(string path, string? scanHash, bool verifyHash) {
        try {
            var file = new FileInfo(path);
            if (!file.Exists) {
                _validatedScanFiles.Remove(path);
                return null;
            }
            // Unchanged scans/toggles reuse validation; new scan identity and launch verify bytes.
            if (!verifyHash && _validatedScanFiles.TryGetValue(path, out var validated)
                && string.Equals(validated.ScanHash, scanHash, StringComparison.OrdinalIgnoreCase)
                && validated.Length == file.Length && validated.Modified == file.LastWriteTimeUtc)
                return validated.Hash;
            _validatedScanFiles.Remove(path);
            using var stream = File.OpenRead(path);
            var hash = Convert.ToHexString(SHA512.HashData(stream));
            _validatedScanFiles[path] = (hash, file.Length, file.LastWriteTimeUtc, scanHash);
            return hash;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (ArgumentException) { return null; }
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
        _ = RequestRefresh();
    }
}