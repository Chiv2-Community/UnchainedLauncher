using System.Collections.Generic;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.GUI.ViewModels.ServersTab;

namespace UnchainedLauncher.GUI.Views.DesignInstances {
    public static class ServersTabInstances {
        public static ServersTabVM DEFAULT => new ServersTabVM(
            SettingsViewModelInstances.DEFAULT,
            () => ModListViewModelInstances.DEFAULTMODMANAGER,
            new MessageBoxSpawner(),
            null,
            null);
    }
}
