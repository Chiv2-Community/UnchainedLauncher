using UnchainedLauncher.GUI.ViewModels;
using UnchainedLauncher.GUI.ViewModels.Registry;

namespace UnchainedLauncher.GUI.Views.DesignInstances {
    public static class MainWindowViewModelInstances {
        public static MainWindowVM DEFAULT => new MainWindowVM(
            LauncherViewModelInstances.DEFAULT,
            ModListViewModelInstances.DEFAULT,
            SettingsViewModelInstances.DEFAULT,
            ServersTabInstances.DEFAULT,
            RegistryTabVM.DEFAULT
        );
    }
}