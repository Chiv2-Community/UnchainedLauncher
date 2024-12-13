using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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