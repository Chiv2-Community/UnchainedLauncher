using System;
using System.ComponentModel;
using UnchainedLauncher.GUI.ViewModels.ServersTab;

namespace UnchainedLauncher.GUI.ViewModels {
    public partial class MainWindowVM : INotifyPropertyChanged, IDisposable {
        public LauncherVM LauncherViewModel { get; }
        public ModListVM ModListViewModel { get; }
        public SettingsVM SettingsViewModel { get; }
        public ServersTabVM ServersTab { get; }

        public MainWindowVM(LauncherVM launcherViewModel,
                            ModListVM modListViewModel,
                            SettingsVM settingsViewModel,
                            ServersTabVM serversTab) {
            LauncherViewModel = launcherViewModel;
            ModListViewModel = modListViewModel;
            SettingsViewModel = settingsViewModel;
            ServersTab = serversTab;
        }

        public void Dispose() {
            SettingsViewModel.Dispose();
            // TODO: Servers tab needs to be disposable
        }
    }
}