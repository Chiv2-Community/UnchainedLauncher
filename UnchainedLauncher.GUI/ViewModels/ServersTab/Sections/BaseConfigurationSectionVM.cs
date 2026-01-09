using System.Collections.ObjectModel;
using PropertyChanged;
using UnchainedLauncher.GUI.ViewModels.ServersTab.IniSections;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab.Sections {
    [AddINotifyPropertyChangedInterface]
    public class BaseConfigurationSectionVM {
        public BaseConfigurationSectionVM(
            TBLGameModeSectionVM gameMode,
            TBLGameUserSettingsSectionVM userSettings,
            GameSessionSectionVM gameSession,
            ObservableCollection<string> availableMaps
        ) {
            GameMode = gameMode;
            UserSettings = userSettings;
            GameSession = gameSession;
            AvailableMaps = availableMaps;
        }

        public TBLGameModeSectionVM GameMode { get; }
        public TBLGameUserSettingsSectionVM UserSettings { get; }
        public GameSessionSectionVM GameSession { get; }

        public ObservableCollection<string> AvailableMaps { get; }
    }
}