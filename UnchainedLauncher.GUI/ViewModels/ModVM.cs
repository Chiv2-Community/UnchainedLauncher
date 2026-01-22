using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using LanguageExt.Common;
using LanguageExt.UnsafeValueAccess;
using log4net;
using PropertyChanged;
using Semver;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.PakDir;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.UnrealModScanner.JsonModels;

namespace UnchainedLauncher.GUI.ViewModels {
    using static LanguageExt.Prelude;

    [AddINotifyPropertyChangedInterface]
    public partial class ModVM {
        private static readonly ILog Logger = LogManager.GetLogger(nameof(ModVM));
        // A ModViewModel needs access to the mod manager so that it can enable/disable releases as they get set on the view.
        private IModManager ModManager { get; }
        private IPakDir PakDir { get; }
        public Mod Mod { get; }
        
        // Download progress tracking
        public double DownloadProgress { get; set; }
        public double DownloadProgressRemaining => 100.0 - DownloadProgress;
        public bool IsDownloading { get; set; }
        public bool IsDownloaded { get; set; }

        public VersionNameSort VersionNameSortKey => new VersionNameSort(
            Optional(EnabledRelease).Map(x => x.Version).FirstOrDefault(),
            Mod.LatestReleaseInfo.Name
        );

        public Release? EnabledRelease { get; set; }

        // The version selected in the dropdown (not necessarily enabled/downloaded)
        public Release? SelectedVersion { get; set; }

        public string Description {
            get {
                var displayedRelease = SelectedVersion ?? Mod.LatestRelease.FirstOrDefault();
                var description = Optional(displayedRelease).Match(
                    None: Mod.LatestReleaseInfo.Description,
                    Some: x => x.Info.Description
                );

                var depenencies =
                    (displayedRelease ?? Mod.LatestRelease)
                        .ToList()
                        .Bind(r => ModManager.GetAllDependenciesForRelease(r));

                var depMessage =
                    !depenencies.Any()
                        ? ""
                        : depenencies.Fold(
                            "You must also enable the dependencies below:\n",
                            (aggMessage, dep) => aggMessage + $"- {dep.Info.Name} {dep.Version}\n"
                        );

                return description + "\n\n" + depMessage;
            }
        }

        public string IconUrl =>
            SelectedVersion?.Info.NonEmptyIconUrl
            ?? Mod.LatestReleaseInfo.NonEmptyIconUrl
            ?? "pack://application:,,,/UnchainedLauncher;component/assets/chiv2-unchained-logo.png";

        public AssetCollections? Manifest => SelectedVersion?.Manifest ?? Mod.LatestRelease.FirstOrDefault()?.Manifest;

        public bool HasMarkers => Manifest?.Markers?.Any() ?? false;
        public bool HasBlueprints => Manifest?.Blueprints?.Any() ?? false;
        public bool HasMaps => Manifest?.Maps?.Any() ?? false;
        public bool HasReplacements => Manifest?.Replacements?.Any() ?? false;
        public bool HasArbitrary => Manifest?.Arbitrary?.Any() ?? false;

        public bool HasContents => HasMarkers || HasBlueprints || HasMaps || HasReplacements || HasArbitrary;

        private List<Release> GetDependenciesForCurrentRelease() =>
            (EnabledRelease ?? Mod.LatestRelease)
                .ToList()
                .Bind(r => ModManager.GetAllDependenciesForRelease(r))
                .ToList();

        public bool HasDependencies => GetDependenciesForCurrentRelease().Any();

        public List<Release> Dependencies => GetDependenciesForCurrentRelease();

        public string ButtonText {
            get {
                if (EnabledRelease == null) {
                    return "Download";
                }

                // Check if selected version is different from enabled version
                var selectedVersion = SelectedVersion ?? Mod.LatestRelease.FirstOrDefault();
                if (selectedVersion != null && selectedVersion.Tag != EnabledRelease.Tag) {
                    return "Change Version";
                }

                return "Remove";
            }
        }


        public bool IsEnabled => EnabledRelease != null;

        public string EnabledVersion =>
                Optional(EnabledRelease).Match(
                    None: () => "none",
                    Some: x => x.Tag
                );

        public List<string> AvailableVersions => Mod.Releases.Select(x => x.Tag).ToList();

        public string GithubReleaseUrl => (EnabledRelease?.ReleaseUrl) ?? Mod.LatestReleaseInfo.RepoUrl;

        public ModVM(Mod mod, Option<Release> enabledRelease, IModManager modManager, IPakDir pakDir) {
            EnabledRelease = enabledRelease.ValueUnsafe();

            Mod = mod;
            ModManager = modManager;
            PakDir = pakDir;

            // Initialize SelectedVersion to the latest release
            SelectedVersion = Mod.LatestRelease.FirstOrDefault();

            // Check if pak is already downloaded
            var coords = Optional(EnabledRelease).Map(ReleaseCoordinates.FromRelease);
            IsDownloaded = coords.Map(c => PakDir.GetInstalledPakFile(c).IsSome).IfNone(false);

            PropertyChangedEventManager.AddHandler(this, (sender, e) => {
                if (e.PropertyName == nameof(EnabledRelease)) {
                    UpdateCurrentlyEnabledVersion(EnabledRelease);
                    // Trigger dependent UI updates via re-evaluation (weaving should raise for EnabledRelease)
                }
            }, nameof(EnabledRelease));

            Logger.Debug($"Initialized ModViewModel for {mod.LatestReleaseInfo.Name}. Currently enabled release: {EnabledVersion}");
        }

        public Option<UpdateCandidate> CheckForUpdate() {
            return (Optional(EnabledRelease), Mod.LatestRelease)
                .Sequence()
                .Bind(x => UpdateCandidate.CreateIfNewer(x.Item1, x.Item2));
        }

        private async Task DownloadAndEnableRelease(Release release, ReleaseCoordinates coords) {
            IsDownloading = true;
            DownloadProgress = 0;

            try {
                var progress = new Progress<double>(p => {
                    DownloadProgress = p;
                    Logger.Debug($"Download progress: {p}%");
                });

                var result = await PakDir.Install(
                    coords,
                    (outputPath) => ModManager.ModRegistry.DownloadPak(coords, outputPath).MapLeft(e => Error.New(e)),
                    release.PakFileName,
                    Some<IProgress<double>>(progress)
                ).ToEither();

                result.Match(
                    Right: _ => {
                        IsDownloaded = true;
                        EnabledRelease = release;
                        Logger.Info($"Successfully downloaded and enabled {coords}");
                    },
                    Left: error => {
                        Logger.Error($"Failed to download {coords}: {error.Message}");
                        IsDownloaded = false;
                    }
                );
            }
            finally {
                IsDownloading = false;
            }
        }

        [RelayCommand]
        private async Task EnableOrDisable() {
            if (EnabledRelease == null) {
                // Use the selected version from the dropdown
                var releaseToEnable = SelectedVersion ?? Mod.LatestRelease.FirstOrDefault();
                if (releaseToEnable == null) return;

                var coords = ReleaseCoordinates.FromRelease(releaseToEnable);
                
                // Check if already downloaded
                var isInstalled = PakDir.GetInstalledPakFile(coords).IsSome;
                
                if (!isInstalled) {
                    await DownloadAndEnableRelease(releaseToEnable, coords);
                } else {
                    // Already downloaded, just enable
                    IsDownloaded = true;
                    EnabledRelease = releaseToEnable;
                }
            }
            else {
                // Check if we're changing versions or just removing
                var selectedVersion = SelectedVersion ?? Mod.LatestRelease.FirstOrDefault();
                var isChangingVersion = selectedVersion != null && selectedVersion.Tag != EnabledRelease.Tag;

                if (isChangingVersion) {
                    // Change version: delete old pak and download new one
                    var oldCoords = ReleaseCoordinates.FromRelease(EnabledRelease);
                    var newCoords = ReleaseCoordinates.FromRelease(selectedVersion!);

                    Logger.Info($"Changing version from {oldCoords} to {newCoords}");

                    // Uninstall old version
                    var uninstallResult = PakDir.Uninstall(oldCoords);
                    uninstallResult.Match(
                        Right: _ => Logger.Info($"Successfully uninstalled old version {oldCoords}"),
                        Left: error => Logger.Error($"Failed to uninstall old version {oldCoords}: {error.Message}")
                    );

                    // Download and enable new version
                    EnabledRelease = null;
                    await DownloadAndEnableRelease(selectedVersion, newCoords);
                }
                else {
                    // Just remove: disable and delete the pak
                    var coords = ReleaseCoordinates.FromRelease(EnabledRelease);
                    EnabledRelease = null;
                    IsDownloaded = false;

                    // Uninstall (delete) the pak file
                    var uninstallResult = PakDir.Uninstall(coords);
                    uninstallResult.Match(
                        Right: _ => Logger.Info($"Successfully uninstalled {coords}"),
                        Left: error => Logger.Error($"Failed to uninstall {coords}: {error.Message}")
                    );
                }
            }
        }

        [RelayCommand]
        private void ViewOnGithub() {
            var url = GithubReleaseUrl;
            if (string.IsNullOrWhiteSpace(url))
                return;

            try {
                Process.Start(new ProcessStartInfo {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception e) {
                Logger.Warn($"Failed to open URL: {url}", e);
            }
        }

        public bool UpdateCurrentlyEnabledVersion(Option<Release> newVersion) =>
            newVersion.Match(
                None: () => ModManager.DisableMod(Mod),
                Some: ModManager.EnableModRelease
            );
    }

    public record VersionNameSort(SemVersion? Version, string Name) : IComparable<VersionNameSort> {
        public int CompareTo(VersionNameSort? other) {
            if (other == null)
                return 1;

            if (Version == null)
                return 1;

            if (other.Version == null)
                return -1;

            return Name.CompareTo(other.Name);
        }
    }
}