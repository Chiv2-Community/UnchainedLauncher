using System.Collections.ObjectModel;
using UnchainedLauncher.Core.JsonModels.Metadata.V4;
using UnchainedLauncher.GUI.ViewModels.ServersTab.IniSections;
using UnchainedLauncher.GUI.ViewModels.ServersTab.Sections;

namespace UnchainedLauncher.GUI.Views.Servers.DesignInstances {
    public class BaseConfigurationSectionDesignVM : BaseConfigurationSectionVM {
        public BaseConfigurationSectionDesignVM() : base(
            new TBLGameModeSectionVM(),
            new TBLGameUserSettingsSectionVM(),
            new GameSessionSectionVM(),
            new ObservableCollection<Chivalry2Map>()
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

    public class BalanceSectionDesignVM : BalanceSectionVM {
        public BalanceSectionDesignVM() : base(
            new TBLGameModeSectionVM()
        ) {
        }
    }
}