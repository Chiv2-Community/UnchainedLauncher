using System;
using System.ComponentModel;

namespace UnchainedLauncher.GUI.ViewModels {
    public partial class MainWindowViewModel : INotifyPropertyChanged, IDisposable {
        public LauncherViewModel LauncherViewModel { get; }
        public ModListViewModel ModListViewModel { get; }
        public SettingsViewModel SettingsViewModel { get; }
        public ServerLauncherViewModel ServerLauncherViewModel { get; }
        public ServersViewModel ServersViewModel { get; }

        public MainWindowViewModel(LauncherViewModel launcherViewModel, ModListViewModel modListViewModel, SettingsViewModel settingsViewModel, ServerLauncherViewModel serverLauncherViewModel, ServersViewModel serversViewModel) {
            LauncherViewModel = launcherViewModel;
            ModListViewModel = modListViewModel;
            SettingsViewModel = settingsViewModel;
            ServerLauncherViewModel = serverLauncherViewModel;
            ServersViewModel = serversViewModel;
        }

        public void Dispose() {
            SettingsViewModel.Dispose();
            ServerLauncherViewModel.Dispose();
            ServersViewModel.Dispose();
        }
    }
}