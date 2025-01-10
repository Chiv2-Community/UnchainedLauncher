using System.Collections.Generic;
using UnchainedLauncher.GUI.ViewModels.ServersTab;

namespace UnchainedLauncher.GUI.Views.DesignInstances {
    public static class ServerInfoFormViewModelInstances {
        public static ServerInfoFormVM DEFAULT => new ServerInfoFormVM(new List<string> { "FFA_Courtyard", "FFA_Wardenglade" });
    }
}
