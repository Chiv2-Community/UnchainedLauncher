using System;
using System.ComponentModel;
using UnchainedLauncher.GUI.ViewModels.ServersTab;
using UnchainedLauncher.UnrealModScanner.GUI.ViewModels;

namespace UnchainedLauncher.GUI.ViewModels {
    public partial class MainWindowVM : INotifyPropertyChanged, IDisposable {
        public HomeVM HomeVM { get; }
        public ModListVM ModListViewModel { get; }
        public ModScanTabVM ModScanTabVM { get; }
        public SettingsVM SettingsViewModel { get; }
        public ServersTabVM ServersTab { get; }

        public MainWindowVM(HomeVM launcherVM,
                            ModListVM modListViewModel,
                            SettingsVM settingsViewModel,
                            ServersTabVM serversTab) {
            HomeVM = launcherVM;
            ModListViewModel = modListViewModel;
            SettingsViewModel = settingsViewModel;
            ServersTab = serversTab;
            ModScanTabVM = new ModScanTabVM();
        }

        public void Dispose() {
            SettingsViewModel.Dispose();
            // TODO: Servers tab needs to be disposable
        }
    }
}