using UnchainedLauncher.GUI.ViewModels;

namespace UnchainedLauncher.GUI.Views.DesignInstances {
    public static class ServersViewModelInstances {
        public static ServersViewModel DEFAULT => CreateDefaultServersViewModel();

        private static ServersViewModel CreateDefaultServersViewModel() {
            var instance = new ServersViewModel(SettingsViewModelInstances.DEFAULT, null);
            instance.Servers.Add(ServerViewModelInstances.DEFAULT);
            instance.Servers.Add(ServerViewModelInstances.DEFAULT);
            return instance;
        }
    }
}