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
            AppendLog("Mocking installation log...").Start();
            AppendLog("Doing things...").Start();
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

            await AppendLog("🚀 Selected version: v" + release.Version);
            await AppendLog("📍 Installation targets:\n    " + string.Join("\n    ", from t in targets select t.FullName));
            await AppendLog("");

            foreach (var target in targets!) {
                await AppendLog("-----------------------------------------------------");
                await AppendLog($"🛠️ Installing v{release.Version} to {target}");
                await _installer.Install(target, release, false, AppendLog);
                await AppendLog("-----------------------------------------------------");
                await AppendLog("");
            }

            await AppendLog("✅ Installation complete!");
            await AppendLog("🎉 You can now launch Chivalry 2 as you normally would and the unchained launcher will handle everything from there. Enjoy!");

            MessageBox.Show("Chivalry 2 Unchained Launcher has been installed successfully! 🚀\n\nLaunch Chivalry 2 like normal to start Unchained.");
            CanContinue = true;
        }

        public Task Continue() {
            return Task.CompletedTask;
        }

        private async Task AppendLog(string appendString) {
            Log += appendString + "\n";

            // People think nothing happened when things are too fast. Slow them down just so they believe.
            await Task.Delay(100 + Random.Shared.Next(0, 500));
        }
    }
}