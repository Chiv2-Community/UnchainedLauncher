using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.GUI.ViewModels;

namespace UnchainedLauncher.GUI.Views.DesignInstances {
    public static class LauncherViewModelInstances {
        public static LauncherVM DEFAULT => new LauncherDesignVM();
    }

    public class LauncherDesignVM : LauncherVM {
        public LauncherDesignVM() : base(
            SettingsViewModelInstances.DEFAULT,
            null,
            null,
            null,
            null,
            new MessageBoxSpawner()
        ) { }
    }
}