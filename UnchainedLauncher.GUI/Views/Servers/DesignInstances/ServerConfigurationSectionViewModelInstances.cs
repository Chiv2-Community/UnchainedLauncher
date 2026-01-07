using UnchainedLauncher.GUI.ViewModels.ServersTab.Sections;

namespace UnchainedLauncher.GUI.Views.Servers.DesignInstances {
    public class BaseConfigurationSectionDesignVM : BaseConfigurationSectionVM {
        public BaseConfigurationSectionDesignVM() : base(new ServerConfigurationDesignVM()) {
        }
    }

    public class AdvancedConfigurationSectionDesignVM : AdvancedConfigurationSectionVM {
        public AdvancedConfigurationSectionDesignVM() : base(new ServerConfigurationDesignVM()) {
        }
    }
}
