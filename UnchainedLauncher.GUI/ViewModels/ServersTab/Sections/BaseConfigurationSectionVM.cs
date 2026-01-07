using System.Collections.ObjectModel;
using UnchainedLauncher.GUI.ViewModels.ServersTab.IniSections;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab.Sections {
    public class BaseConfigurationSectionVM {
        private readonly ServerConfigurationVM _parent;

        public BaseConfigurationSectionVM(ServerConfigurationVM parent) {
            _parent = parent;
        }

        public TBLGameModeSectionVM GameMode => _parent.GameMode;
        public TBLGameUserSettingsSectionVM UserSettings => _parent.UserSettings;
        public GameSessionSectionVM GameSession => _parent.GameSession;

        public ObservableCollection<string> AvailableMaps => _parent.AvailableMaps;
    }
}
