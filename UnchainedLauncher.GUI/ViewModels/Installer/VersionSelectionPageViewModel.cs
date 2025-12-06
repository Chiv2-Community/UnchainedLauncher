using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using Semver;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.GUI.Services;

namespace UnchainedLauncher.GUI.ViewModels.Installer {

    public partial class VersionSelectionPageViewModel : IInstallerPageViewModel, INotifyPropertyChanged {
        private readonly IReleaseLocator _releaseLocator;
        public string TitleText => "Select UnchainedLauncher version you wish to install";
        public string DescriptionText => "The latest stable version is recommended. After choosing your version and selecting \"Install\" the Unchained Launcher Installer will begin the installation process.";

        public string ContinueButtonText => "Install";
        public bool CanContinue => SelectedVersion != null;

        public string GoBackButtonText => "Back";
        public bool CanGoBack => true;

        public bool ShowDevReleases { get; set; }

        public ObservableCollection<ReleaseTarget> AvailableVersions { get; set; }
        public IEnumerable<ReleaseTarget> VisibleVersions => AvailableVersions.Filter(ShouldShowVersion);

        public ReleaseTarget? SelectedVersion { get; set; }
        public string SelectedVersionDescriptionHtml => SelectedVersion == null ? "" : MarkdownRenderer.RenderHtml(SelectedVersion.DescriptionMarkdown);

        public bool IsSelected { get { return SelectedVersion != null; } }

        public VersionSelectionPageViewModel() : this(null) {
            AvailableVersions.Add(new ReleaseTarget("test", "#foo\n\nBar.", new SemVersion(1, 2), new List<ReleaseAsset>(), DateTimeOffset.Now, true, false));
            SelectLatestVersion();
        }


        public VersionSelectionPageViewModel(IReleaseLocator releaseLocator) {
            _releaseLocator = releaseLocator;
            AvailableVersions = new ObservableCollection<ReleaseTarget>();

            AvailableVersions.CollectionChanged += (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VisibleVersions)));
        }

        public Task Continue() {
            return Task.CompletedTask;
        }

        public async Task Load() {
            var releases = await _releaseLocator.GetAllReleases();
            if (!releases.Any()) {
                MessageBox.Show("Failed to fetch UnchainedLauncher releases. Please check your internet connection and try again.");
                return;
            }

            AvailableVersions.Clear();
            releases.ToList().ForEach(release => AvailableVersions.Add(release));
            SelectLatestVersion();
        }

        private void SelectLatestVersion() {
            SelectedVersion = AvailableVersions.Filter(x => x.IsLatestStable).FirstOrDefault();
        }

        private bool ShouldShowVersion(ReleaseTarget release) {
            if (!ShowDevReleases) {
                // Github releases have an idea of prerelease, and then semver
                // also does. If ShowDevReleases is false, then we don't show
                // anything which is considered a prerelease by either.
                return !release.IsPrerelease;
            }

            return true;
        }

        [RelayCommand]
        private void ViewOnGithub() {
            if (SelectedVersion == null) {
                MessageBox.Show("Please select a version to view.");
                return;
            }

            Process.Start(new ProcessStartInfo {
                FileName = SelectedVersion.PageUrl,
                UseShellExecute = true
            });
        }

    }
}