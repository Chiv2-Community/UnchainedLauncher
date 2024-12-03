using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LanguageExt;
using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using UnchainedLauncher.Core.JsonModels;
using System.ComponentModel;
using Microsoft.Win32;
using System.Xaml.Schema;
using System.Threading.Tasks;
using System.Windows;
using PropertyChanged;
using log4net;
using System.Diagnostics;
using Markdig;
using UnchainedLauncher.Core.Installer;

namespace UnchainedLauncher.GUI.ViewModels.Installer {

    public partial class VersionSelectionPageViewModel : IInstallerPageViewModel, INotifyPropertyChanged {
        public readonly IUnchainedLauncherInstaller Installer;
        public string TitleText => "Select UnchainedLauncher version you wish to install";
        public string DescriptionText => "The latest stable version is recommended. After choosing your version and selecting \"Install\" the Unchained Launcher Installer will begin the installation process.";

        public string ContinueButtonText => "Install";
        public bool CanContinue => SelectedVersion != null;

        public string GoBackButtonText => "Back";
        public bool CanGoBack => true;

        public bool ShowDevReleases { get; set; }

        public ObservableCollection<VersionedRelease> AvailableVersions { get; set; }
        public IEnumerable<VersionedRelease> VisibleVersions => AvailableVersions.Filter(ShouldShowVersion);

        public VersionedRelease? SelectedVersion { get; set; }
        public string SelectedVersionDescriptionHtml { get {
                if (SelectedVersion == null) return "";
                else return RenderMarkdown(SelectedVersion!.Release.Body); 
        } }

        public bool IsSelected { get { return SelectedVersion != null; } }
        public ICommand ViewOnGithubCommand { get; }

        public VersionSelectionPageViewModel() : this(new MockInstaller()) { 
            VersionedRelease.DefaultMockReleases.ToList().ForEach(AvailableVersions.Add);
            SelectLatestVersion();
        }


        public VersionSelectionPageViewModel(IUnchainedLauncherInstaller installer) {
            Installer = installer;
            AvailableVersions = new ObservableCollection<VersionedRelease>();

            AvailableVersions.CollectionChanged += (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VisibleVersions)));

            ViewOnGithubCommand = new RelayCommand(OpenGithubPage);
        }

        public Task Continue() {
            return Task.CompletedTask;
        }

        public async Task Load() {
            var releases = await Installer.GetAllReleases();
            if(releases == null || !releases.Any()) {
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

        private bool ShouldShowVersion(VersionedRelease release) {
            if(!ShowDevReleases) {
                // Github action has an idea of prerelease, and then semver
                // also does. If ShowDevReleases is false, then we don't show
                // anything which is considered a prerelease by either.
                return !release.Release.Prerelease && !release.Version.IsPrerelease;
            }
            
            return true;
        }

        private void OpenGithubPage() {
            if(SelectedVersion == null) {
                MessageBox.Show("Please select a version to view.");
                return;
            }

            Process.Start(new ProcessStartInfo {
                FileName = SelectedVersion.Release.HtmlUrl,
                UseShellExecute = true
            });
        }

        private static string RenderMarkdown(string markdown) {
            if (string.IsNullOrEmpty(markdown)) return "";

            var pipeline = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .Build();

            var html = Markdown.ToHtml(markdown, pipeline);
            return $@"
            <html>
                <head>
                    <style>
                        body {{ 
                            font-family: Segoe UI, sans-serif;
                            margin: 0;
                            padding: 0;
                        }}
                        img {{ max-width: 100%; }}
                        pre {{ 
                            background-color: #f6f8fa;
                            padding: 16px;
                            border-radius: 6px;
                            overflow-x: auto;
                        }}
                        code {{ 
                            font-family: Consolas, monospace;
                            background-color: #f6f8fa;
                            padding: 0.2em 0.4em;
                            border-radius: 3px;
                        }}
                    </style>
                </head>
                <body>
                    {html}
                </body>
            </html>";
        }

    }
}
