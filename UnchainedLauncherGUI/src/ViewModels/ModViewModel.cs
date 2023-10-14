using UnchainedLauncherCore.JsonModels.Metadata.V3;
using UnchainedLauncherCore.Mods;
using CommunityToolkit.Mvvm.Input;
using log4net;
using PropertyChanged;
using Semver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace UnchainedLauncherGUI.ViewModels {
    [AddINotifyPropertyChangedInterface]
    public class ModViewModel {
        private static readonly ILog logger = LogManager.GetLogger(nameof(ModViewModel));
        // A ModViewModel needs access to the mod manager so that it can enable/disable releases as they get set on the view.
        private ModManager ModManager { get; }

        public Mod Mod { get; }

        public VersionNameSort VersionNameSortKey => new VersionNameSort(EnabledRelease?.Version, Mod.LatestManifest.Name);

        private Release? _enabledRelease;
        public Release? EnabledRelease {
            get => _enabledRelease;
            set {
                if (_enabledRelease != value) {
                    if (value == null) {
                        ModManager.DisableModRelease(_enabledRelease!);
                    } else {
                        ModManager.EnableModRelease(value);
                    }
                    _enabledRelease = value;
                }
            }
        }
        public string Description {
            get {
                var message = EnabledRelease?.Manifest.Description ?? Mod.LatestManifest.Description;

                var manifest = EnabledRelease?.Manifest ?? Mod.LatestManifest;

                if (manifest.Dependencies.Count > 0) {
                    message += "\n\nYou must also enable the dependencies below:\n";
                    foreach (var dep in manifest.Dependencies) {
                        var mod = ModManager.Mods.FirstOrDefault(x => x.LatestManifest.RepoUrl == dep.RepoUrl);
                        if (mod != null)
                            message += $"- {mod.LatestManifest.Name} {dep.Version}\n";
                    }
                }

                return message;
            }
        }

        public string ButtonText {
            get {
                if (EnabledRelease == null)
                    return "Enable";

                return "Disable";
            }
        }


        public string TagsString {
            get { return string.Join(", ", Mod.LatestManifest.Tags); }
        }

        public bool IsEnabled {
            get { return EnabledRelease != null; }
        }

        public string? EnabledVersion {
            get {
                if (IsEnabled)
                    return EnabledRelease!.Tag;

                return "none";
            }
        }

        public List<string> AvailableVersions {
            get { return Mod.Releases.Select(x => x.Tag).ToList(); }
        }

        public ICommand ButtonCommand { get; }

        public ModViewModel(Mod mod, Release? enabledRelease, ModManager modManager) {
            _enabledRelease = enabledRelease;
            EnabledRelease = _enabledRelease;

            Mod = mod;
            ModManager = modManager;

            ButtonCommand = new RelayCommand(DisableOrEnable);

            logger.Debug($"Initialized ModViewModel for {mod.LatestManifest.Name}. Currently enabled release: {enabledRelease?.Tag ?? "None"}");
        }

        private void DisableOrEnable() {
            if (EnabledRelease == null)
                EnabledRelease = Mod.Releases.First();
            else
                EnabledRelease = null;
        }
    }

    public record VersionNameSort(SemVersion? Version, string Name) : IComparable<VersionNameSort> {
        public int CompareTo(VersionNameSort? other) {
            if(other == null)
                return 1;

            if (Version == null)
                return 1;

            if (other.Version == null)
                return -1;

            return Name.CompareTo(other.Name);
        }
    }
}
