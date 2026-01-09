using PropertyChanged;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab.Sections {
    [AddINotifyPropertyChangedInterface]
    public class TdmConfigurationSectionVM {
        public TdmConfigurationSectionVM(int? tdmTimeLimit = null, int? tdmTicketCount = null) {
            TDMTimeLimit = tdmTimeLimit;
            TDMTicketCount = tdmTicketCount;
        }

        public int? TDMTimeLimit { get; set; }
        public int? TDMTicketCount { get; set; }
    }
}