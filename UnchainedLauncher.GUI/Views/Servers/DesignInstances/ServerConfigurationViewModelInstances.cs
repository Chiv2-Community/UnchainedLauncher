using UnchainedLauncher.GUI.ViewModels.ServersTab;

namespace UnchainedLauncher.GUI.Views.Servers.DesignInstances {
    public static class ServerConfigurationViewModelInstances {
        public static ServerConfigurationVM DEFAULT => new ServerConfigurationDesignVM();
    }

    public class ServerConfigurationDesignVM : ServerConfigurationVM {
        public ServerConfigurationDesignVM() : base(Mods.DesignInstances.ModListViewModelInstances.DEFAULTMODMANAGER) {
        }
    }
}