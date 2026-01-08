namespace UnchainedLauncher.GUI.ViewModels.ServersTab.Sections {
    public class FfaConfigurationSectionVM {
        private readonly ServerConfigurationVM _parent;

        public FfaConfigurationSectionVM(ServerConfigurationVM parent) {
            _parent = parent;
        }

        public int? FFATimeLimit {
            get => _parent.FFATimeLimit;
            set => _parent.FFATimeLimit = value;
        }

        public int? FFAScoreLimit {
            get => _parent.FFAScoreLimit;
            set => _parent.FFAScoreLimit = value;
        }
    }
}
