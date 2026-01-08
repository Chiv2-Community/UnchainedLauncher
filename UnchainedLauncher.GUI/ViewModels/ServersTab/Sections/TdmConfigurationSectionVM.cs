namespace UnchainedLauncher.GUI.ViewModels.ServersTab.Sections {
    public class TdmConfigurationSectionVM {
        private readonly ServerConfigurationVM _parent;

        public TdmConfigurationSectionVM(ServerConfigurationVM parent) {
            _parent = parent;
        }

        public int? TDMTimeLimit {
            get => _parent.TDMTimeLimit;
            set => _parent.TDMTimeLimit = value;
        }

        public int? TDMTicketCount {
            get => _parent.TDMTicketCount;
            set => _parent.TDMTicketCount = value;
        }
    }
}
