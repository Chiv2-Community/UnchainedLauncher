using System;
using System.ComponentModel;
using UnchainedLauncher.GUI.ViewModels.ServersTab;

namespace UnchainedLauncher.GUI.ViewModels {
    public partial class MainWindowVM : INotifyPropertyChanged, IDisposable {
        public LauncherVM LauncherViewModel { get; }
        public ModListVM ModListViewModel { get; }
        public SettingsVM SettingsViewModel { get; }
        public ServerLauncherVM ServerLauncherViewModel { get; }
        public ServersVM ServersViewModel { get; }
        public ServersTabVM ServersTab { get; }

        public MainWindowVM(LauncherVM launcherViewModel,
                            ModListVM modListViewModel,
                            SettingsVM settingsViewModel,
                            ServerLauncherVM serverLauncherViewModel,
                            ServersVM serversViewModel,
                            ServersTabVM serversTab) {
            LauncherViewModel = launcherViewModel;
            ModListViewModel = modListViewModel;
            SettingsViewModel = settingsViewModel;
            ServerLauncherViewModel = serverLauncherViewModel;
            ServersViewModel = serversViewModel;
            ServersTab = serversTab;
        }

        public void Dispose() {
            SettingsViewModel.Dispose();
            ServerLauncherViewModel.Dispose();
            ServersViewModel.Dispose();
        }
    }
}