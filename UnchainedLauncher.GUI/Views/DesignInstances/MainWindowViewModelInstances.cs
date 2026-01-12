using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.GUI.ViewModels;
using UnchainedLauncher.GUI.Views.Mods.DesignInstances;
using UnchainedLauncher.GUI.Views.Servers.DesignInstances;

namespace UnchainedLauncher.GUI.Views.DesignInstances {
    public static class MainWindowViewModelInstances {
        public static MainWindowVM DEFAULT => new MainWindowDesignVM();
    }

    public class MainWindowDesignVM : MainWindowVM {
        public MainWindowDesignVM() : base(
            LauncherViewModelInstances.DEFAULT,
            ModListViewModelInstances.DEFAULT,
            SettingsViewModelInstances.DEFAULT,
            ServersTabInstances.DEFAULT,
            new AggregateModRegistry()
        ) {
        }
    }
}