using PropertyChanged;
using UnchainedLauncher.GUI.ViewModels.ServersTab.IniSections;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab.Sections {
    [AddINotifyPropertyChangedInterface]
    public class AdvancedConfigurationSectionVM {
        public AdvancedConfigurationSectionVM(
            IpNetDriverSectionVM ipNetDriver,
            TBLGameModeSectionVM gameMode,
            bool showInServerBrowser,
            int? playerBotCount,
            int? warmupTime,
            string additionalCLIArgs
        ) {
            IpNetDriver = ipNetDriver;
            GameMode = gameMode;
            ShowInServerBrowser = showInServerBrowser;
            PlayerBotCount = playerBotCount;
            WarmupTime = warmupTime;
            AdditionalCLIArgs = additionalCLIArgs;
        }

        public IpNetDriverSectionVM IpNetDriver { get; }
        public TBLGameModeSectionVM GameMode { get; }

        public int? PlayerBotCount { get; set; }
        public int? WarmupTime { get; set; }
        public bool ShowInServerBrowser { get; set; }
        
        public string AdditionalCLIArgs { get; set; }
    }
}