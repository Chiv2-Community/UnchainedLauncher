using UnchainedLauncher.GUI.ViewModels.ServersTab;

namespace UnchainedLauncher.GUI.Views.Servers.DesignInstances {
    public static class ServerInfoFormViewModelInstances {
        public static ServerInfoFormVM DEFAULT => new ServerInfoFormDesignVM();
    }

    public class ServerInfoFormDesignVM : ServerInfoFormVM {
        public ServerInfoFormDesignVM() : base() { }
    }
}