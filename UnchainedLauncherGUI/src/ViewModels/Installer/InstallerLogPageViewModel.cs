using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using UnchainedLauncher.Core.Installer;

namespace UnchainedLauncher.GUI.ViewModels.Installer
{
    public partial class InstallerLogPageViewModel: IInstallerPageViewModel, INotifyPropertyChanged
    {
        public string TitleText => "Installation Log";
        public string? DescriptionText => null;
        public string ContinueButtonText => "Finish";
        public string GoBackButtonText => "Back";
        public bool CanContinue { get; set; }
        public bool CanGoBack => false;

        public string Log { get; set; }

        private readonly IUnchainedLauncherInstaller Installer;
        private readonly Func<IEnumerable<DirectoryInfo>> GetInstallationTargets;
        private readonly Func<VersionedRelease> GetSelectedVersion;

        public InstallerLogPageViewModel() : this(
            new MockInstaller(),
            () => new List<DirectoryInfo>(),
            () => VersionedRelease.DefaultMockReleases.First()
        ) { 
            AppendLog("Mocking installation log...");
            AppendLog("Doing things...");
        }

        public InstallerLogPageViewModel(IUnchainedLauncherInstaller installer, Func<IEnumerable<DirectoryInfo>> getTargets, Func<VersionedRelease> getSelectedVersion) {
            Installer = installer;

            CanContinue = false;
            Log = "";

            GetInstallationTargets = getTargets;
            GetSelectedVersion = getSelectedVersion;
        }

        public async Task Load() {
            var targets = GetInstallationTargets();
            var version = GetSelectedVersion();

            AppendLog("Selected version: " + version.Release.TagName);
            AppendLog("Installation targets:\n    " + string.Join("\n    ", targets.Select(t => t.FullName)));
            AppendLog("");

            if(targets == null || version == null) {
                AppendLog("Error: Installation targets or version not found.");
                return;
            }

            foreach(var target in targets!) {
                AppendLog("-----------------------------------------------------");
                AppendLog($"Installing {version.Release.TagName} to {target}");
                await Installer.Install(target, version, false, AppendLog);
                AppendLog("-----------------------------------------------------");
                AppendLog("");
            }

            AppendLog("Installation complete!");
            AppendLog("You can now launch Chivalry 2 as you normally would and the unchained launcher will handle everything from there.");

            MessageBox.Show("Chivalry 2 Unchained Launcher has been installed successfully!");
            CanContinue = true;
        }

        public Task Continue() {
            return Task.CompletedTask;
        }

        public void AppendLog(string appendString) {
            Log += appendString + "\n";
        }
    }
}
