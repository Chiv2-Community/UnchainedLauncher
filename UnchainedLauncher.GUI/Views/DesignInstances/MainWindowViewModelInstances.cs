using UnchainedLauncher.GUI.ViewModels;

namespace UnchainedLauncher.GUI.Views.DesignInstances {
    public static class MainWindowViewModelInstances {
        public static MainWindowViewModel DEFAULT => new MainWindowViewModel(
            LauncherViewModelInstances.DEFAULT,
            ModListViewModelInstances.DEFAULT,
            SettingsViewModelInstances.DEFAULT,
            ServerLauncherViewModelInstances.DEFAULT,
            ServersViewModelInstances.DEFAULT
        );
    }
}