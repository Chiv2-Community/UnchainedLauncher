using C2GUILauncher.JsonModels;
using C2GUILauncher.Mods;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace C2GUILauncher.ViewModels {
    [AddINotifyPropertyChangedInterface]
    public class ModViewModel {
        // A ModViewModel needs access to the mod manager so that it can enable/disable releases as they get set on the view.
        private ModManager ModManager { get; }

        public Mod Mod { get; }

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

            Mod = mod;
            ModManager = modManager;

            ButtonCommand = new RelayCommand(DisableOrEnable);
        }

        private void DisableOrEnable() {
            if(EnabledRelease == null)
                EnabledRelease = Mod.Releases.First();
            else
                EnabledRelease = null;
        }
    }
}
