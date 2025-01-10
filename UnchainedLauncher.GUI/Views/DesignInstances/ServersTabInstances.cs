using System.Collections.ObjectModel;
using UnchainedLauncher.GUI.ViewModels.ServersTab;

namespace UnchainedLauncher.GUI.Views.DesignInstances {
    public static class ServersTabInstances {
        public static ServersTabVM DEFAULT => new ServersTabVM(
            ServersViewModelInstances.DEFAULT,
            new ObservableCollection<ServerTemplateVM> { new ServerTemplateVM() }
            );
    }
}
