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
    private readonly Dictionary<string, (string Hash, long Length, DateTime Modified)> _validatedScanFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly IModManager _modManager;
    private readonly IUnmanagedPakSettings? _settings;
    private readonly List<MapDto> _defaultMaps;

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
    private void OnMetadataRefreshed(object? sender, EventArgs args) => ReconcileCatalog();

    private void OnModStateChanged() {
        ReconcileCatalog();
        ModStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ReconcileCatalog(bool verifyHashes = false) {
        var releases = _modManager.GetEnabledAndDependencyReleases(includeDependencies: _settings?.AutomaticallyDownloadPakDependencies ?? true).ToList();
        var inactivePaths = _modManager.Mods.SelectMany(mod => mod.Releases)
            .SelectMany(release => release.Manifest.RelevantBlueprints())
            .Select(ServerBlueprintIdentity.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        inactivePaths.ExceptWith(releases.SelectMany(release => release.Manifest.RelevantBlueprints())
            .Select(ServerBlueprintIdentity.Key));
        // V1 ownership is in memory; registry-only catalogs do not need the pak inventory.
        var managedPaths = _cachedScanManifest?.Paks.Count > 0 ? _modManager.PakDir.ManagedPaks.Select(pak =>
            Path.GetFullPath(Path.Combine(_modManager.PakDir.DirPath, pak.PakFileName)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase) : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Scans must not resurrect disabled managed actors.
        var manifests = releases.Select(release => release.Manifest)
            .Concat(_cachedScanManifest?.Paks.Where(pak => !string.IsNullOrWhiteSpace(pak.PakPath)
                && !managedPaths.Contains(Path.GetFullPath(pak.PakPath)) && IsCurrentScan(pak, verifyHashes)).Select(pak => pak.Inventory with {
                    Blueprints = pak.Inventory.Blueprints.Where(actor => !inactivePaths.Contains(ServerBlueprintIdentity.Key(actor))).ToList()
                }) ?? []).ToList();
        Reconcile(AvailableMaps, _defaultMaps.Concat(manifests.SelectMany(manifest => manifest.RelevantMaps())));
        Reconcile(AvailableServerModBlueprints, manifests.SelectMany(manifest => manifest.RelevantBlueprints()));
    }

    // External pak changes do not raise mod-manager events.
    public void RefreshCatalog() => ReconcileCatalog(verifyHashes: true);

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

    private bool IsCurrentScan(PakManifest pak, bool verifyHash) {
        if (string.IsNullOrWhiteSpace(pak.PakHash)) return false;
        try {
            var file = new FileInfo(pak.PakPath);
            if (!file.Exists) {
                _validatedScanFiles.Remove(pak.PakPath);
                return false;
            }
            var stamp = (pak.PakHash, file.Length, file.LastWriteTimeUtc);
            // Toggle/metadata events reuse validation; launch rehashes even if metadata is unchanged.
            if (!verifyHash && _validatedScanFiles.TryGetValue(pak.PakPath, out var validated) && validated == stamp)
                return true;
            _validatedScanFiles.Remove(pak.PakPath);
            using var stream = File.OpenRead(pak.PakPath);
            if (!string.Equals(Convert.ToHexString(SHA512.HashData(stream)), pak.PakHash, StringComparison.OrdinalIgnoreCase))
                return false;
            _validatedScanFiles[pak.PakPath] = stamp;
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (ArgumentException) { return false; }
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
        _validatedScanFiles.Clear();
        ReconcileCatalog();
    }
}