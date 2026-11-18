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
        public string? GoBackButtonText => CanGoBack ? "Back" : null;
        public bool CanContinue { get; set; }
        public bool CanGoBack { get; private set; }

        public List<DirectoryInfo> SuccessfulTargets { get; } = new List<DirectoryInfo>();

        public string Log { get; set; }

        private readonly IUnchainedLauncherInstaller _installer;
        private readonly Func<IEnumerable<DirectoryInfo>> _getInstallationTargets;
        private readonly Func<ReleaseTarget> _getSelectedRelease;

        public InstallerLogPageViewModel() : this(
            new MockInstaller(),
            () => new List<DirectoryInfo>(),
            () => new ReleaseTarget("", "", new SemVersion(0, 0), Array.Empty<ReleaseAsset>(), DateTimeOffset.Now, true, false)
        ) {
        }

        public InstallerLogPageViewModel(IUnchainedLauncherInstaller installer, Func<IEnumerable<DirectoryInfo>> getTargets, Func<ReleaseTarget> getSelectedRelease) {
            _installer = installer;

            CanContinue = false;
            Log = "";

            _getInstallationTargets = getTargets;
            _getSelectedRelease = getSelectedRelease;
        }

        public async Task Load() {
            CanContinue = false;
            CanGoBack = false;
            SuccessfulTargets.Clear();
            Log = "";
            var targets = _getInstallationTargets().ToList();
            if (targets.Count == 0) {
                await AppendLog("❌ No installation targets selected. Go back and select an installation location.");
                CanGoBack = true;
                return;
            }
            var release = _getSelectedRelease();

            await AppendLog("🚀 Selected version: v" + release.Version);
            await AppendLog("📍 Installation targets:\n    " + string.Join("\n    ", from t in targets select t.FullName));
            await AppendLog("");

            foreach (var target in targets!) {
                await AppendLog("-----------------------------------------------------");
                await AppendLog($"🛠️ Installing v{release.Version} to {target}");
                bool installed;
                try {
                    installed = await _installer.Install(target, release, false, AppendLog);
                }
                catch (Exception ex) {
                    installed = false;
                    await AppendLog(ex.ToString());
                }
                if (installed) {
                    SuccessfulTargets.Add(target);
                    await AppendLog($"✅ Installed to {target.FullName}");
                }
                else {
                    await AppendLog($"❌ Installation failed for {target.FullName}");
                }
                await AppendLog("-----------------------------------------------------");
                await AppendLog("");
            }

            if (SuccessfulTargets.Count != targets.Count) {
                await AppendLog($"❌ Installed to {SuccessfulTargets.Count} of {targets.Count} locations. Review the errors above, then go back to retry or change installation locations.");
                CanGoBack = true;
                return;
            }

            await AppendLog("✅ Installation complete!");
            await AppendLog("🎉 You can now launch Chivalry 2 as you normally would and the unchained launcher will handle everything from there. Enjoy!");

            MessageBox.Show("Chivalry 2 Unchained Launcher has been installed successfully! 🚀\n\nLaunch Chivalry 2 like normal to start Unchained.");
            CanContinue = true;
        }

        public Task Continue() {
            return Task.CompletedTask;
        }

        private Task AppendLog(string appendString) {
            Log += appendString + "\n";
            return Task.CompletedTask;
        }
    }
}
