using UnchainedLauncher.GUI.ViewModels.ServersTab.IniSections;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab.Sections {
    public class AdvancedConfigurationSectionVM {
        private readonly ServerConfigurationVM _parent;

        public AdvancedConfigurationSectionVM(ServerConfigurationVM parent) {
            _parent = parent;
        }

        public IpNetDriverSectionVM IpNetDriver => _parent.IpNetDriver;
        public TBLGameModeSectionVM GameMode => _parent.GameMode;
    }
}
