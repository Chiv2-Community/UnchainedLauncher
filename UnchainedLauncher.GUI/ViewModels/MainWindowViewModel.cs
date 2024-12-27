using System;
using System.ComponentModel;

namespace UnchainedLauncher.GUI.ViewModels {
    public partial class MainWindowViewModel : INotifyPropertyChanged, IDisposable {

        public SettingsViewModel SettingsViewModel { get; }
        public ServerLauncherViewModel ServerLauncherViewModel { get; }
        public ServersViewModel ServersViewModel { get; }

        public MainWindowViewModel(SettingsViewModel settingsViewModel, ServerLauncherViewModel serverLauncherViewModel, ServersViewModel serversViewModel) {
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