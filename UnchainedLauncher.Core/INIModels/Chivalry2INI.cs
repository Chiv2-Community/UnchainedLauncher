using log4net;
using StructuredINI;
using UnchainedLauncher.Core.INIModels.Engine;
using UnchainedLauncher.Core.INIModels.Game;
using UnchainedLauncher.Core.INIModels.GameUserSettings;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.INIModels;

[INIFile]
public record EngineINI(IpNetDriver _ipNetDriver) {
    public static EngineINI Default = new EngineINI(new IpNetDriver());
    public IpNetDriver IpNetDriver => _ipNetDriver ?? Default.IpNetDriver;
};

[INIFile]
public record GameINI(
    GameSession _gameSession,
    TBLGameMode _tBLGameMode,
    LTSGameMode _lTSGameMode,
    ArenaGameMode _arenaGameMode
) {
    public static GameINI Default = new GameINI(
        new GameSession(),
        new TBLGameMode(),
        new LTSGameMode(),
        new ArenaGameMode()
    );

    public GameSession GameSession => _gameSession ?? Default.GameSession;
    public TBLGameMode TBLGameMode => _tBLGameMode ?? Default.TBLGameMode;
    public LTSGameMode LTSGameMode => _lTSGameMode ?? Default.LTSGameMode;
    public ArenaGameMode ArenaGameMode => _arenaGameMode ?? Default.ArenaGameMode;
};

[INIFile]
public record GameUserSettingsINI(
    TBLGameUserSettings _tBLGameUserSettings
) {
    public static GameUserSettingsINI Default => new GameUserSettingsINI(
        new TBLGameUserSettings()
    );

    public TBLGameUserSettings TBLGameUserSettings => _tBLGameUserSettings ?? Default.TBLGameUserSettings;
}

public record Chivalry2INI(
    EngineINI Engine,
    GameINI Game,
    GameUserSettingsINI GameUserSettings) {
    private static readonly ILog Logger = LogManager.GetLogger(nameof(Chivalry2INI));

    public static Chivalry2INI LoadINIProfile(string profileName) {
        var iniDir = FilePaths.Chiv2ConfigPath(profileName);

        var enginePath = Path.Combine(iniDir, "Engine.ini");
        var gamePath = Path.Combine(iniDir, "Game.ini");
        var gameUserSettingsPath = Path.Combine(iniDir, "GameUserSettings.ini");

        var engine = StructuredINIReader.LoadOrDefault(enginePath, EngineINI.Default);
        var game = StructuredINIReader.LoadOrDefault(gamePath, GameINI.Default);
        var gameUserSettings = StructuredINIReader.LoadOrDefault(gameUserSettingsPath, GameUserSettingsINI.Default);

        return new Chivalry2INI(
            engine,
            game,
            gameUserSettings
        );
    }

    public bool SaveINIProfile(string profileName) {
        var iniDir = FilePaths.Chiv2ConfigPath(profileName);

        var enginePath = Path.Combine(iniDir, "Engine.ini");
        var gamePath = Path.Combine(iniDir, "Game.ini");
        var gameUserSettingsPath = Path.Combine(iniDir, "GameUserSettings.ini");

        return StructuredINIWriter.Save(enginePath, Engine) && StructuredINIWriter.Save(gamePath, Game) && StructuredINIWriter.Save(gameUserSettingsPath, GameUserSettings);
    }


}