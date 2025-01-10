using UnchainedLauncher.GUI.ViewModels;

namespace UnchainedLauncher.GUI.Views.DesignInstances {
    public static class MainWindowViewModelInstances {
        public static MainWindowVM DEFAULT => new MainWindowVM(
            LauncherViewModelInstances.DEFAULT,
            ModListViewModelInstances.DEFAULT,
            SettingsViewModelInstances.DEFAULT,
            ServerLauncherViewModelInstances.DEFAULT,
            ServersViewModelInstances.DEFAULT,
            ServersTabInstances.DEFAULT
        );
    }
}