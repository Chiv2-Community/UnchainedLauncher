using StructuredINI;
using StructuredINI.Codecs;

namespace UnchainedLauncher.Core.INIModels.Game;

[INISection("/Script/Engine.GameSession")]
public record GameSession(int MaxPlayers = 64);

[INISection("/Script/TBL.TBLGameMode")]
public record TBLGameMode(
    string ServerName = "MyLocalServer",
    string ServerIdentifier = "id",
    bool BotBackfillEnabled = true,
    int BotBackfillLowPlayers = 10,
    int BotBackfillLowBots = 12,
    int BotBackfillHighPlayers = 30,
    int BotBackfillHighBots = 0,
    float MinTimeBeforeStartingMatch = 10,
    float MaxTimeBeforeStartingMatch = 60,
    int IdleKickTimerSpectate = 180,
    int IdleKickTimerDisconnect = 360,
    string[]? MapList = null,
    int MapListIndex = -1,
    bool bHorseCompatibleServer = false,
    AutoBalance[]? TeamBalanceOptions = null,
    AutoBalance[]? AutoBalanceOptions = null,
    int StartOfMatchGracePeriodForAutoBalance = 60,
    int StartOfMatchGracePeriodForTeamSwitching = 30,
    bool bUseStrictTeamBalanceEnforcement = false
) {
    private static readonly string[] DefaultMapList = { "FFA_Courtyard", "FFA_Wardenglade" };

    public string[] MapList { get; init; } = MapList ?? DefaultMapList;
    public AutoBalance[] TeamBalanceOptions { get; init; } = TeamBalanceOptions ?? [];
    public AutoBalance[] AutoBalanceOptions { get; init; } = AutoBalanceOptions ?? [];
}

[INISection("/Script/TBL.LTSGameMode")]
public record LTSGameMode(
    int PreCountdownDelay = 5,
    int Rounds = 9
);

[INISection("/Script/TBL.ArenaGameMode")]
public record ArenaGameMode(
    int Rounds = 39,
    int RoundTimeLimit = 300,
    bool bClearWeaponsPostRound = true,
    bool bClearHorsesPostRound = true,
    bool bResetTaggedActorsPostRound = true,
    bool bUsePreCountdownForCustomizationLoading = true,
    int TimeBetweenRounds = 30,
    int TeamLives = 0
);