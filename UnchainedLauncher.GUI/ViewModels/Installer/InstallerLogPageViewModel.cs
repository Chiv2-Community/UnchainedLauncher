using Semver;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Installer;

namespace UnchainedLauncher.GUI.ViewModels.Installer {
    public partial class InstallerLogPageViewModel : IInstallerPageViewModel, INotifyPropertyChanged {
        public string TitleText => "Installation Log";
        public string? DescriptionText => null;
        public string ContinueButtonText => "Finish";
        public string? GoBackButtonText => null;
        public bool CanContinue { get; set; }
        public bool CanGoBack => false;

        public string Log { get; set; }

        private readonly IUnchainedLauncherInstaller _installer;
        private readonly Func<IEnumerable<DirectoryInfo>> _getInstallationTargets;
        private readonly Func<ReleaseTarget> _getSelectedRelease;

        public InstallerLogPageViewModel() : this(
            new MockInstaller(),
            () => new List<DirectoryInfo>(),
            () => new ReleaseTarget("", "", new SemVersion(0, 0), Array.Empty<ReleaseAsset>(), DateTimeOffset.Now, true, false)
        ) {
            AppendLog("Mocking installation log...");
            AppendLog("Doing things...");
        }

        public InstallerLogPageViewModel(IUnchainedLauncherInstaller installer, Func<IEnumerable<DirectoryInfo>> getTargets, Func<ReleaseTarget> getSelectedRelease) {
            _installer = installer;

            CanContinue = false;
            Log = "";

            _getInstallationTargets = getTargets;
            _getSelectedRelease = getSelectedRelease;
        }

        public async Task Load() {
            var targets = _getInstallationTargets();
            var release = _getSelectedRelease();

            AppendLog("Selected version: v" + release.Version);
            AppendLog("Installation targets:\n    " + string.Join("\n    ", from t in targets select t.FullName));
            AppendLog("");

            foreach (var target in targets!) {
                AppendLog("-----------------------------------------------------");
                AppendLog($"Installing v{release.Version} to {target}");
                await _installer.Install(target, release, false, AppendLog);
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