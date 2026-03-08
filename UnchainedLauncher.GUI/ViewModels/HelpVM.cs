using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;

namespace UnchainedLauncher.GUI.ViewModels {
    public partial class HelpVM {
        public SettingsVM SettingsViewModel { get; }

        public HelpVM(SettingsVM settingsViewModel) {
            SettingsViewModel = settingsViewModel;
        }

        [RelayCommand]
        private void OpenDiscord() {
            Process.Start(new ProcessStartInfo("https://discord.gg/chiv2unchained") { UseShellExecute = true });
        }

        [RelayCommand]
        private void OpenWiki() {
            Process.Start(new ProcessStartInfo("https://unchained.wiki") { UseShellExecute = true });
        }
    }
}
