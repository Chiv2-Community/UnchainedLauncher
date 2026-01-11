using PropertyChanged;
using System.Collections.ObjectModel;
using UnchainedLauncher.Core.JsonModels.Metadata.V4;
using UnchainedLauncher.GUI.ViewModels.ServersTab.IniSections;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab.Sections {
    [AddINotifyPropertyChangedInterface]
    public class BaseConfigurationSectionVM {
        public BaseConfigurationSectionVM(
            TBLGameModeSectionVM gameMode,
            TBLGameUserSettingsSectionVM userSettings,
            GameSessionSectionVM gameSession,
            ObservableCollection<Chivalry2Map> availableMaps
        ) {
            GameMode = gameMode;
            UserSettings = userSettings;
            GameSession = gameSession;
            AvailableMaps = availableMaps;
        }

        public TBLGameModeSectionVM GameMode { get; }
        public TBLGameUserSettingsSectionVM UserSettings { get; }
        public GameSessionSectionVM GameSession { get; }

        public ObservableCollection<Chivalry2Map> AvailableMaps { get; }
    }
}