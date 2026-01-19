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
using UnchainedLauncher.Core.JsonModels.Metadata.V4;
using UnchainedLauncher.Core.Services.Mods;

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


        public string Description {
            get {
                var description = Optional(EnabledRelease).Match(
                    None: Mod.LatestReleaseInfo.Description,
                    Some: x => x.Info.Description
                );

                var depenencies =
                    (EnabledRelease ?? Mod.LatestRelease)
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

        public string ButtonText =>
            Optional(EnabledRelease).Match(
                None: () => "Enable",
                Some: _ => "Disable"
            );


        public bool IsEnabled => EnabledRelease != null;

        public string EnabledVersion =>
                Optional(EnabledRelease).Match(
                    None: () => "none",
                    Some: x => x.Tag
                );

        public List<string> AvailableVersions => Mod.Releases.Select(x => x.Tag).ToList();

        public string GithubReleaseUrl => (EnabledRelease?.ReleaseUrl) ?? Mod.LatestReleaseInfo.RepoUrl;

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
                EnabledRelease = Mod.Releases.First();
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