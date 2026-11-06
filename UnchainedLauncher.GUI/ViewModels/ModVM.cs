using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using log4net;
using Semver;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.UnrealModScanner.JsonModels;

namespace UnchainedLauncher.GUI.ViewModels {
    using static LanguageExt.Prelude;

    public partial class ModVM : INotifyPropertyChanged {
        private static readonly ILog Logger = LogManager.GetLogger(nameof(ModVM));
        // A ModViewModel needs access to the mod manager so that it can enable/disable releases as they get set on the view.
        private IModManager ModManager { get; }
        public Mod Mod { get; }

        public VersionNameSort VersionNameSortKey => new VersionNameSort(
            Optional(EnabledRelease).Map(x => x.Version).FirstOrDefault(),
            Mod.LatestReleaseInfo.Name
        );

        public Release? EnabledRelease { get; set; }
        public Release? DependencyRelease { get; set; }
        public bool IsDependency => EnabledRelease == null && DependencyRelease != null;
        public Release? SelectedRelease {
            get => EnabledRelease ?? DependencyRelease;
            set {
                if (value != SelectedRelease) EnabledRelease = value;
            }
        }


        public string Description {
            get {
                var description = Optional(SelectedRelease).Match(
                    None: Mod.LatestReleaseInfo.Description,
                    Some: x => x.Info.Description
                );

                var depenencies =
                    (SelectedRelease ?? Mod.LatestRelease)
                        .ToList()
                        .Bind(r => ModManager.GetAllDependenciesForRelease(r));

                var depMessage =
                    !depenencies.Any()
                        ? ""
                        : depenencies.Fold(
                            "Declared pak dependencies (automatic downloads are controlled in the footer):\n",
                            (aggMessage, dep) => aggMessage + $"- {dep.Info.Name} {dep.Version}\n"
                        );

                return description + "\n\n" + depMessage;
            }
        }

        public string IconUrl =>
            SelectedRelease?.Info.NonEmptyIconUrl
            ?? Mod.LatestReleaseInfo.NonEmptyIconUrl
            ?? "pack://application:,,,/UnchainedLauncher;component/assets/chiv2-unchained-logo.png";

        public AssetCollections? Manifest => SelectedRelease?.Manifest ?? Mod.LatestRelease.FirstOrDefault()?.Manifest;

        public bool HasMarkers => Manifest?.Markers?.Any() ?? false;
        public bool HasBlueprints => Manifest?.Blueprints?.Any() ?? false;
        public bool HasMaps => Manifest?.Maps?.Any() ?? false;
        public bool HasReplacements => Manifest?.Replacements?.Any() ?? false;
        public bool HasArbitrary => Manifest?.Arbitrary?.Any() ?? false;

        public bool HasContents => HasMarkers || HasBlueprints || HasMaps || HasReplacements || HasArbitrary;

        public bool HasDependencies =>
            (SelectedRelease ?? Mod.LatestRelease)
                .ToList()
                .Bind(r => ModManager.GetAllDependenciesForRelease(r))
                .Any();

        public List<Release> Dependencies =>
            (SelectedRelease ?? Mod.LatestRelease)
                .ToList()
                .Bind(r => ModManager.GetAllDependenciesForRelease(r))
                .ToList();

        public string ButtonText => IsDependency ? "Dependency" : EnabledRelease != null ? "Enabled" : "Enable";
        public string ButtonToolTip => IsDependency ? "Required automatically. Click to enable explicitly."
            : EnabledRelease != null ? "Click to remove the explicit selection. Required dependencies remain automatic."
            : "Click to enable explicitly.";


        public bool IsEnabled => SelectedRelease != null;

        public string EnabledVersion =>
                Optional(SelectedRelease).Match(
                    None: () => "none",
                    Some: x => x.Tag
                );

        public List<string> AvailableVersions => Mod.Releases.Select(x => x.Tag).ToList();

        public string GithubReleaseUrl => (SelectedRelease?.ReleaseUrl) ?? Mod.LatestReleaseInfo.RepoUrl;

        public ModVM(Mod mod, Option<Release> enabledRelease, IModManager modManager) {
            EnabledRelease = enabledRelease.ValueUnsafe();

            Mod = mod;
            ModManager = modManager;

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

        [RelayCommand]
        private void EnableOrDisable() {
            if (EnabledRelease == null)
                EnabledRelease = DependencyRelease ?? Mod.Releases.First();
            else
                EnabledRelease = null;
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

        [RelayCommand]
        private void OpenGithubRelease() {
            var url = GithubReleaseUrl;
            if (string.IsNullOrWhiteSpace(url)) return;
            try {
                Process.Start(new ProcessStartInfo {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex) {
                Logger.Warn($"Failed to open URL: {url}", ex);
            }
        }
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