using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using log4net;
using Semver;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.GUI.Services;

namespace UnchainedLauncher.GUI.ViewModels.Installer {

    public partial class VersionSelectionPageViewModel : IInstallerPageViewModel, INotifyPropertyChanged {
        private static readonly ILog _logger = LogManager.GetLogger(typeof(VersionSelectionPageViewModel));

        private readonly IReleaseLocator _releaseLocator;
        private readonly IVersionExtractor _versionExtractor;
        public string TitleText => "🚀 Choose Your Unchained Version";
        public string DescriptionText => "The latest stable version is highly recommended for the best experience. Choose a version and click \"Install\" to begin.";

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

        public VersionSelectionPageViewModel() : this(null, new FileInfoVersionExtractor()) {
            AvailableVersions.Add(new ReleaseTarget("test", "#foo\n\nBar.", new SemVersion(1, 2), new List<ReleaseAsset>(), DateTimeOffset.Now, true, false));
            SelectLatestVersion();
        }


        public VersionSelectionPageViewModel(IReleaseLocator releaseLocator, IVersionExtractor versionExtractor) {
            _releaseLocator = releaseLocator;
            _versionExtractor = versionExtractor;
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
            var currentVersion = GetCurrentVersion();
            if (currentVersion is { IsPrerelease: true }) {
                ShowDevReleases = true;
                var currentRelease = AvailableVersions.FirstOrDefault(v => currentVersion.MatchesRelease(v.Version));
                if (currentRelease != null) {
                    SelectedVersion = currentRelease;
                    return;
                }
            }

            SelectedVersion = AvailableVersions.FirstOrDefault(x => x.IsLatestStable);
        }

        private SemVersion? GetCurrentVersion() {
            var currentPath = Environment.ProcessPath;
            return currentPath != null && System.IO.File.Exists(currentPath) ? _versionExtractor.GetVersion(currentPath) : null;
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