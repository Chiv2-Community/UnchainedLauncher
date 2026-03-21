using PropertyChanged;
using System;
using System.Collections.ObjectModel;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.GUI.ViewModels.ServersTab.IniSections;
using UnchainedLauncher.UnrealModScanner.JsonModels;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab.Sections {
    [AddINotifyPropertyChangedInterface]
    public class BaseConfigurationSectionVM(
        TBLGameModeSectionVM gameMode,
        TBLGameUserSettingsSectionVM userSettings,
        GameSessionSectionVM gameSession,
        ObservableCollection<MapDto> availableMaps,
        bool desyncPatch,
        CensorArg censorMode) {

        public TBLGameModeSectionVM GameMode { get; } = gameMode;
        public TBLGameUserSettingsSectionVM UserSettings { get; } = userSettings;
        public GameSessionSectionVM GameSession { get; } = gameSession;
        public bool DesyncPatch { get; set; } = desyncPatch;
        public CensorArg CensorMode { get; set; } = censorMode;

        public bool IsFpsLimitEnabled => !DesyncPatch;

        public string FpsLimitToolTip => DesyncPatch
            ? "FPS Limit is disabled because Desync Patch is enabled. Desync Patch makes this field do nothing."
            : "MaxFPS / FrameRateLimit for the server. Important: players should match this FPS to avoid desync issues. Example: 80";

        public CensorArg[] AllCensorModes { get; } = Enum.GetValues<CensorArg>();

        public ObservableCollection<MapDto> AvailableMaps { get; } = availableMaps;
    }
}