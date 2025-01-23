using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using log4net;
using Semver;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services.Mods;

namespace UnchainedLauncher.GUI.ViewModels {
    using static LanguageExt.Prelude;

    public partial class ModVM : INotifyPropertyChanged {
        private static readonly ILog logger = LogManager.GetLogger(nameof(ModVM));
        // A ModViewModel needs access to the mod manager so that it can enable/disable releases as they get set on the view.
        private IModManager ModManager { get; }
        public Mod Mod { get; }

        public VersionNameSort VersionNameSortKey => new VersionNameSort(
            Optional(EnabledRelease).Map(x => x.Version).FirstOrDefault(),
            Mod.LatestManifest.Name
        );

        public Release? EnabledRelease { get; set; }

        public string Description {
            get {
                var description = Optional(EnabledRelease).Match(
                    None: Mod.LatestManifest.Description,
                    Some: x => x.Manifest.Description
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
                            (aggMessage, dep) => aggMessage + $"- {dep.Manifest.Name} {dep.Version}\n"
                        );

                return description + "\n\n" + depMessage;
            }
        }

        public string ButtonText =>
            Optional(EnabledRelease).Match(
                None: () => "Enable",
                Some: _ => "Disable"
            );


        public string TagsString => string.Join(", ", Mod.LatestManifest.Tags);

        public bool IsEnabled => EnabledRelease != null;

        public string EnabledVersion =>
                Optional(EnabledRelease).Match(
                    None: () => "none",
                    Some: x => x.Tag
                );

        public List<string> AvailableVersions => Mod.Releases.Select(x => x.Tag).ToList();

        public ICommand ButtonCommand { get; }

        public ModVM(Mod mod, Option<Release> enabledRelease, IModManager modManager) {
            EnabledRelease = enabledRelease.ValueUnsafe();

            Mod = mod;
            ModManager = modManager;

            ButtonCommand = new RelayCommand(DisableOrEnable);

            PropertyChangedEventManager.AddHandler(this, async (sender, e) => {
                if (e.PropertyName == nameof(EnabledRelease)) {
                    UpdateCurrentlyEnabledVersion(EnabledRelease);
                }
            }, nameof(EnabledRelease));

            logger.Debug($"Initialized ModViewModel for {mod.LatestManifest.Name}. Currently enabled release: {EnabledVersion}");
        }

        public Option<UpdateCandidate> CheckForUpdate() {
            return (Optional(EnabledRelease), Mod.LatestRelease)
                .Sequence()
                .Bind(x => UpdateCandidate.CreateIfNewer(x.Item1, x.Item2));
        }

        private void DisableOrEnable() {
            if (EnabledRelease == null)
                EnabledRelease = Mod.Releases.First();
            else
                EnabledRelease = null;
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