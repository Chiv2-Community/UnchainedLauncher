using UnchainedLauncher.GUI.ViewModels;

namespace UnchainedLauncher.GUI.Views.DesignInstances {
    public static class ServersViewModelInstances {
        public static ServersVM DEFAULT => CreateDefaultServersViewModel();

        private static ServersVM CreateDefaultServersViewModel() {
            var instance = new ServersVM(SettingsViewModelInstances.DEFAULT, null);
            instance.Servers.Add(ServerViewModelInstances.DEFAULT);
            instance.Servers.Add(ServerViewModelInstances.DEFAULT);
            return instance;
        }
    }
}