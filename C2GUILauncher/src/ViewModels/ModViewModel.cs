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

        /// <summary>
        /// The mod manager is used to enable/disable mods/releases as they get set on the view.
        /// </summary>
        private ModManager ModManager { get; }

        /// <summary>
        /// The mod that this view model represents.
        /// </summary>
        public Mod Mod { get; }

        /// <summary>
        /// The underlying enabled release for this mod, or null if no release is enabled.
        /// </summary>
        private Release? _enabledRelease;

        /// <summary>
        /// The public accessor and setter for the enabled release for this mod, or null if no release is enabled.
        /// 
        /// The get accessor will return null if the enabled release is not in the list of releases for this mod.
        /// 
        /// The setter will enable the given release in the mod manager if it is not already enabled, as well as any dependencies of the given release.
        /// When disabling or changing the enabled release, the setter will also disable any releases that depend on the given release.
        /// </summary>
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

        /// <summary>
        /// If the download for this mod failed, this will be the exception that caused the download to fail.
        /// This is tracked by subscribing to the FailedDownloads collection on the mod manager.
        /// </summary>
        public Exception? DownloadError { get; set; }
        public bool DownloadFailed { get { return DownloadError != null; } }

        /// <summary>
        /// This is the description that will be displayed in the mod list.
        /// It includes any dependencies that the mod has, as well as any download errors.
        /// </summary>
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

        /// <summary>
        /// This is the text that will be displayed on the button for this mod.
        /// If the download failed, it will say "Retry Download", and the button will retry the download
        /// </summary>
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

        /// <summary>
        /// This is the text that will be displayed in the version column for this mod.
        /// </summary>
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

        /// <summary>
        /// The ButtonCommand is bound to the button on the view, and will either disable the mod if it is enabled, or retry the download if it failed.
        /// </summary>
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

        /// <summary>
        /// Disable the mod if it is enabled, or retry the download if it failed.
        /// </summary>
        private void DisableOrRetry() {
            if (DownloadFailed && EnabledRelease != null) {
                ModManager.EnableModRelease(EnabledRelease);
            } else {
                EnabledRelease = null;
            }
        }

        /// <summary>
        /// Track changes to the failed downloads, and update the download error if this mod is in the failed downloads.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
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

        /// <summary>
        /// Track changes to the enabled mod releases, and update the enabled release if it is changed.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
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
