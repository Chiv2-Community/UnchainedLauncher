using C2GUILauncher.JsonModels;
using C2GUILauncher.Mods;
using CommunityToolkit.Mvvm.Input;
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
                    if (_enabledRelease != null) {
                        var result = ModManager.DisableModRelease(_enabledRelease, false, false);

                        if (!result.Successful) {
                            var message = value == null
                                ? $"Failed to disable mod because another active mod depends on this mod. Disable this mod and all mods that depend on it?"
                                : $"Failed to change mod version because another active mod depends on this mod version. Change this mod version anyway?";

                            var dependentsNameAndVersionString = string.Join("\n", result.Dependents.Select(x => $"- {x.Manifest.Name} {x.Tag}"));
                            var shouldCascade = value == null;

                            message += $"\n\nDependents:\n{dependentsNameAndVersionString}";

                            var messageBoxResult = MessageBox.Show(
                                message,
                                "Error",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Error
                            );

                            if (messageBoxResult == MessageBoxResult.Yes) {
                                ModManager.DisableModRelease(_enabledRelease, true, shouldCascade);
                            } else {
                                return;
                            }
                        }
                    }

                    if (value != null) {
                        ModManager.EnableModRelease(value);
                    }

                    _enabledRelease = value;
                }
            }
        }

        public Exception? DownloadError { get; set; }
        public bool DownloadFailed { get { return DownloadError != null; } }

        public string Description {
            get {
                var message = EnabledRelease?.Manifest.Description ?? Mod.LatestManifest.Description;

                var manifest = EnabledRelease?.Manifest ?? Mod.LatestManifest;

                if (manifest.Dependencies.Count > 0) {
                    message += "\n\nDependencies:\n";
                    foreach (var dep in manifest.Dependencies) {
                        var mod = ModManager.Mods.FirstOrDefault(x => x.LatestManifest.RepoUrl == dep.RepoUrl);
                        if (mod == null)
                            message += $"-Dependency not found: {dep.RepoUrl} {dep.Version}\n";
                        else
                            message += $"- {mod.LatestManifest.Name} {dep.Version}\n";
                    }
                }

                if (DownloadFailed)
                    message += "\n\nError: " + DownloadError!.Message;

                return message;
            }
        }

        public string ButtonText {
            get {
                if (DownloadFailed)
                    return "Retry Download";

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
                if (DownloadFailed)
                    return "Error";

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
            DownloadError = null;

            ModManager.FailedDownloads.CollectionChanged += FailedDownloads_CollectionChanged;
            ModManager.EnabledModReleases.CollectionChanged += EnabledModReleases_CollectionChanged;

            ButtonCommand = new RelayCommand(DisableOrRetry);
        }

        private void DisableOrRetry() {
            if (DownloadFailed && EnabledRelease != null) {
                ModManager.EnableModRelease(EnabledRelease);
            } else {
                EnabledRelease = null;
            }
        }

        private void FailedDownloads_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
            // Find this mod if it was added to the failed downloads
            var thisDownloadFailed =
                e.NewItems?
                    .Cast<ModReleaseDownloadTask>()
                    .FirstOrDefault(failedDownload => failedDownload.Release.Manifest.RepoUrl == this.Mod.LatestManifest.RepoUrl);

            if (thisDownloadFailed != null)
                DownloadError = thisDownloadFailed.DownloadTask.Task.Exception;

            // Find this mod if it was removed from the failed downloads
            var thisDownloadNoLongerFailed =
                e.OldItems?
                    .Cast<ModReleaseDownloadTask>()
                    .FirstOrDefault(failedDownload => failedDownload.Release.Manifest.RepoUrl == this.Mod.LatestManifest.RepoUrl);

            if (thisDownloadNoLongerFailed != null)
                DownloadError = null;
        }

        private void EnabledModReleases_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
            if (IsEnabled) {
                var isRemoved = e.OldItems?.Cast<Release>()
                        .Any(removedRelease => removedRelease == EnabledRelease);

                if (isRemoved ?? false)
                    EnabledRelease = null;
            }

            var enabledRelease =
                e.NewItems?.Cast<Release>()
                    .FirstOrDefault(newRelease => newRelease.Manifest.RepoUrl == Mod.LatestManifest.RepoUrl);

            if (enabledRelease != null)
                EnabledRelease = enabledRelease;
        }
    }
}
