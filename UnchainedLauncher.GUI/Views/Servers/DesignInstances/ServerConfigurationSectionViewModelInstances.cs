using UnchainedLauncher.GUI.ViewModels.ServersTab.Sections;
using UnchainedLauncher.GUI.ViewModels.ServersTab.IniSections;
using System.Collections.ObjectModel;

namespace UnchainedLauncher.GUI.Views.Servers.DesignInstances {
    public class BaseConfigurationSectionDesignVM : BaseConfigurationSectionVM {
        public BaseConfigurationSectionDesignVM() : base(
            new TBLGameModeSectionVM(),
            new TBLGameUserSettingsSectionVM(),
            new GameSessionSectionVM(),
            new ObservableCollection<string>()
        ) {
        }
    }

    public class AdvancedConfigurationSectionDesignVM : AdvancedConfigurationSectionVM {
        public AdvancedConfigurationSectionDesignVM() : base(
            new IpNetDriverSectionVM(),
            new TBLGameModeSectionVM(),
            showInServerBrowser: true,
            playerBotCount: 0,
            warmupTime: 0
        ) {
        }
    }
}