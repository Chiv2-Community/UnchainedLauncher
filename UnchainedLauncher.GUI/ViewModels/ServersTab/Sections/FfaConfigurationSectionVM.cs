using PropertyChanged;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab.Sections {
    [AddINotifyPropertyChangedInterface]
    public class FfaConfigurationSectionVM {
        public FfaConfigurationSectionVM(int? ffaTimeLimit = null, int? ffaScoreLimit = null) {
            FFATimeLimit = ffaTimeLimit;
            FFAScoreLimit = ffaScoreLimit;
        }

        public int? FFATimeLimit { get; set; }
        public int? FFAScoreLimit { get; set; }
    }
}