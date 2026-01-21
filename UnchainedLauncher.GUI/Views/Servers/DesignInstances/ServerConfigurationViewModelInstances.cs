using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.GUI.ViewModels.ServersTab;
using UnchainedLauncher.UnrealModScanner.GUI.ViewModels;

namespace UnchainedLauncher.GUI.Views.Servers.DesignInstances {
    public static class ServerConfigurationViewModelInstances {
        public static ServerConfigurationVM DEFAULT => new ServerConfigurationDesignVM();
    }

    public class ServerConfigurationDesignVM : ServerConfigurationVM {
        public ServerConfigurationDesignVM() : base(
            Mods.DesignInstances.ModListViewModelInstances.DEFAULTMODMANAGER,
            new ModScanTabVM(),
            new AvailableModsAndMapsService(
                Mods.DesignInstances.ModListViewModelInstances.DEFAULTMODMANAGER,
                new ModScanTabVM()
            ),
            playerBotCount: 10,
            warmupTime: 10
        ) {
        }
    }
}