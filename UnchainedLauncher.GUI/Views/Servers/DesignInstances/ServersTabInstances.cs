using System.Collections.ObjectModel;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.GUI.ViewModels.ServersTab;
using UnchainedLauncher.GUI.Views.DesignInstances;
using UnchainedLauncher.GUI.Views.Mods.DesignInstances;
using UnchainedLauncher.UnrealModScanner.GUI.ViewModels;

namespace UnchainedLauncher.GUI.Views.Servers.DesignInstances {
    public static class ServersTabInstances {
        public static ServersTabVM DEFAULT => new ServersTabDesignVM();
    }

    public class ServersTabDesignVM : ServersTabVM {
        public ServersTabDesignVM() : base(
            SettingsViewModelInstances.DEFAULT,
            ModListViewModelInstances.DEFAULTMODMANAGER,
            new ModScanTabVM(),
            new MessageBoxSpawner(),
            null,
            new ObservableCollection<ServerConfigurationVM> { ServerConfigurationViewModelInstances.DEFAULT },
            new ChivalryProcessWatcher()
        ) {
        }
    }
}