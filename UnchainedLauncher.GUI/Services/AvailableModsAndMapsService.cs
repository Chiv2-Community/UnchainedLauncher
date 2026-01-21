using LanguageExt;
using log4net;
using Newtonsoft.Json;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
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

    private PakDirManifest? _cachedScanManifest = null;

    public AvailableModsAndMapsService(IModManager modManager, ModScanTabVM modScanTab) {
        AvailableMaps = new ObservableCollection<MapDto>(GetDefaultMaps());
        AvailableServerModBlueprints = new ObservableCollection<BlueprintDto>();

        modManager.GetEnabledAndDependencyReleases()
            .ForEach(x => AddAvailableMod(x.Manifest));

        modManager.ModDisabled += release => RemoveAvailableMod(release.Manifest);
        modManager.ModEnabled += (release, previouslyEnabledVersion) => {
            if (previouslyEnabledVersion != null) {
                var coordinates = ReleaseCoordinates.FromRelease(release) with { Version = previouslyEnabledVersion };
                modManager
                    .GetRelease(coordinates)
                    .IfSome(previousRelease => RemoveAvailableMod(previousRelease.Manifest));
            }

            AddAvailableMod(release.Manifest);
        };

        HandleLatestScanUpdate(modScanTab.LastScanResult);
        modScanTab.PropertyChanged += (_, args) => {
            if (args.PropertyName == nameof(modScanTab.ResultsVisual))
                HandleLatestScanUpdate(modScanTab.LastScanResult);
        };
    }

    public void AddAvailableMod(AssetCollections modPakManifest) {
        var newMaps =
            modPakManifest
                .RelevantMaps()
                .Filter(x => !AvailableMaps.Contains(x))
                .ToArray();

        if (newMaps.Any()) {
            Logger.Debug("Adding Available Maps: ");
            newMaps.ForEach(map => Logger.Debug($"\t{map.MapName ?? map.ClassPath ?? map.Path}"));
            newMaps.ForEach(AvailableMaps.Add);
        }

        var newModBlueprints =
            modPakManifest
                .RelevantBlueprints()
                .Filter(x => !AvailableServerModBlueprints.Contains(x))
                .ToArray();

        if (newModBlueprints.Any()) {
            Logger.Debug("Adding Available Blueprints: ");
            newModBlueprints.ForEach(bp => Logger.Debug($"\t{bp.ModName ?? bp.ClassPath ?? bp.Path}"));
            newModBlueprints.ForEach(AvailableServerModBlueprints.Add);
        }
    }

    public void RemoveAvailableMod(AssetCollections manifest) {
        var maps = manifest.RelevantMaps();
        if (maps.Any()) {
            Logger.Debug("Removing Available Maps: ");
            maps.ForEach(m => Logger.Debug($"\t{m.MapName}"));
            maps.ForEach(AvailableMaps.Remove);
        }

        var blueprints = manifest.RelevantBlueprints();
        if (blueprints.Any()) {
            Logger.Debug("Removing Available Blueprints: ");
            blueprints.ForEach(bp => Logger.Debug($"\t{bp.ModName}"));
            manifest.RelevantBlueprints().ForEach(AvailableServerModBlueprints.Remove);
        }
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

        _cachedScanManifest?.Paks
            .ForEach(x => {
                Logger.Debug($"Removing pak from previous scan ({x.PakName})");
                RemoveAvailableMod(x.Inventory);
            });

        if (scanResult == null) return;
        var manifest = ModManifestConverter.ProcessModScan(scanResult);

        manifest
            .Paks
            .ForEach(x => {
                Logger.Debug($"Adding pak from new scan ({x.PakName})");
                AddAvailableMod(x.Inventory);
            });

        _cachedScanManifest = manifest;
    }
}