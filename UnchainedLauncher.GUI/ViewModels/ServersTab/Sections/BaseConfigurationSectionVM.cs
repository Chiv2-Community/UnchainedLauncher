using PropertyChanged;
using System.Collections.ObjectModel;
using UnchainedLauncher.GUI.ViewModels.ServersTab.IniSections;
using UnchainedLauncher.UnrealModScanner.JsonModels;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab.Sections {
    [AddINotifyPropertyChangedInterface]
    public class BaseConfigurationSectionVM(
        TBLGameModeSectionVM gameMode,
        TBLGameUserSettingsSectionVM userSettings,
        GameSessionSectionVM gameSession,
        ObservableCollection<MapDto> availableMaps) {

        public TBLGameModeSectionVM GameMode { get; } = gameMode;
        public TBLGameUserSettingsSectionVM UserSettings { get; } = userSettings;
        public GameSessionSectionVM GameSession { get; } = gameSession;

        public ObservableCollection<MapDto> AvailableMaps { get; } = availableMaps;
    }
}