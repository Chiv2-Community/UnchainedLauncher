using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.GUI.ViewModels;

namespace UnchainedLauncher.GUI.Views.DesignInstances {
    public static class LauncherViewModelInstances {
        public static HomeVM DEFAULT => new HomeDesignVM();
    }

    public class HomeDesignVM : HomeVM {
        public HomeDesignVM() : base(
            SettingsViewModelInstances.DEFAULT,
            null,
            null,
            null,
            new MessageBoxSpawner(),
            new ChivalryProcessWatcher()
        ) {
        }
    }
}