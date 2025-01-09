using System;
using System.ComponentModel;

namespace UnchainedLauncher.GUI.ViewModels {
    public partial class MainWindowVM : INotifyPropertyChanged, IDisposable {
        public LauncherVM LauncherViewModel { get; }
        public ModListVM ModListViewModel { get; }
        public SettingsVM SettingsViewModel { get; }
        public ServerLauncherVM ServerLauncherViewModel { get; }
        public ServersVM ServersViewModel { get; }

        public MainWindowVM(LauncherVM launcherViewModel, ModListVM modListViewModel, SettingsVM settingsViewModel, ServerLauncherVM serverLauncherViewModel, ServersVM serversViewModel) {
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