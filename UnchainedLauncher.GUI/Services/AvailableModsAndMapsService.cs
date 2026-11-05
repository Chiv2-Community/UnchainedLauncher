using log4net;
using Newtonsoft.Json;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    private readonly List<MapDto> _defaultMaps;

    public AvailableModsAndMapsService(IModManager modManager, ModScanTabVM modScanTab) {
        _modManager = modManager;
        _defaultMaps = GetDefaultMaps().ToList();
        AvailableMaps = new ObservableCollection<MapDto>();
        AvailableServerModBlueprints = new ObservableCollection<BlueprintDto>();

        modManager.ModDisabled += _ => OnModStateChanged();
        modManager.ModEnabled += (_, _) => OnModStateChanged();
        WeakEventManager<IModManager, EventArgs>.AddHandler(modManager, nameof(IModManager.MetadataRefreshed), OnMetadataRefreshed);

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
        var releases = _modManager.GetEnabledAndDependencyReleases().ToList();
        var known = _modManager.Mods.SelectMany(mod => mod.Releases).ToList();
        var inactivePaths = known.Except(releases).SelectMany(release => release.Manifest.RelevantBlueprints())
            .Select(ServerBlueprintIdentity.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var activePaths = releases.SelectMany(release => release.Manifest.RelevantBlueprints())
            .Select(ServerBlueprintIdentity.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        inactivePaths.ExceptWith(activePaths);
        // Without scan contributions, registry catalogs do not need the local pak inventory.
        var managedPaths = _cachedScanManifest?.Paks.Count > 0 ? _modManager.PakDir.ManagedPaks.Select(pak =>
            Path.GetFullPath(Path.Combine(_modManager.PakDir.DirPath, pak.PakFileName)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase) : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Managed paks are installed/removed by the launcher. A scan must not resurrect their disabled actors.
        var manifests = releases.Select(release => release.Manifest)
            .Concat(_cachedScanManifest?.Paks.Where(pak => !string.IsNullOrWhiteSpace(pak.PakPath)
                && !managedPaths.Contains(Path.GetFullPath(pak.PakPath)) && IsCurrentScan(pak, verifyHashes)).Select(pak => pak.Inventory with {
                Blueprints = pak.Inventory.Blueprints.Where(actor => !inactivePaths.Contains(ServerBlueprintIdentity.Key(actor))).ToList()
            }) ?? []).ToList();
        Reconcile(AvailableMaps, _defaultMaps.Concat(manifests.SelectMany(manifest => manifest.RelevantMaps())));
        Reconcile(AvailableServerModBlueprints, manifests.SelectMany(manifest => manifest.RelevantBlueprints()));
    }

    // Called at the launch boundary too: external pak changes do not raise mod-manager events.
    public void RefreshCatalog() => ReconcileCatalog(verifyHashes: true);

    public bool IsConfirmedInactive(BlueprintDto actor) {
        if (AvailableServerModBlueprints.Any(current => ServerBlueprintIdentity.Same(current, actor))) return false;
        var owners = _modManager.Mods.SelectMany(mod => mod.Releases)
            .Where(release => release.Manifest.RelevantBlueprints().Any(known => ServerBlueprintIdentity.Same(known, actor))).ToList();
        if (owners.Count == 0) return false; // Offline/unknown ownership is not evidence of a disabled preference.
        // An enabled version missing from the registry may still provide this actor.
        return !owners.Any(owner => _modManager.EnabledModReleaseCoordinates.Any(coordinates =>
            coordinates.Matches(ModIdentifier.FromRelease(owner)) && _modManager.GetRelease(coordinates).IsNone));
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
            // Ordinary metadata/toggle events reuse successful validation of unchanged files.
            // Launch always hashes again, including replacements that preserve size and timestamp.
            if (!verifyHash && _validatedScanFiles.TryGetValue(pak.PakPath, out var validated) && validated == stamp)
                return true;
            _validatedScanFiles.Remove(pak.PakPath);
            using var stream = File.OpenRead(pak.PakPath);
            if (!string.Equals(Convert.ToHexString(SHA512.HashData(stream)), pak.PakHash, StringComparison.OrdinalIgnoreCase)) {
                return false;
            }
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
