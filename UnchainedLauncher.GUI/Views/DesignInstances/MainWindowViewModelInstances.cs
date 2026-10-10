using UnchainedLauncher.GUI.ViewModels;
using UnchainedLauncher.GUI.Views.Mods.DesignInstances;
using UnchainedLauncher.GUI.Views.Servers.DesignInstances;
using UnchainedLauncher.UnrealModScanner.GUI.ViewModels;

namespace UnchainedLauncher.GUI.Views.DesignInstances {
    public static class MainWindowViewModelInstances {
        public static MainWindowVM DEFAULT => new MainWindowDesignVM();
    }

    public class MainWindowDesignVM() : MainWindowVM(LauncherViewModelInstances.DEFAULT,
        ModListViewModelInstances.DEFAULT,
        SettingsViewModelInstances.DEFAULT,
        ServersTabInstances.DEFAULT,
        new ModScanTabVM(),
        new HelpVM(SettingsViewModelInstances.DEFAULT, null, null, null, null, n => { }));
}