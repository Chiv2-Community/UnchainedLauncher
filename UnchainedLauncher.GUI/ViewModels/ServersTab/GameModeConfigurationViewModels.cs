using DiscriminatedUnions;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab {
    public static class GameModeKinds {
        public const string TO = "TO";
        public const string TDM = "TDM";
        public const string Arena = "Arena";
        public const string LTS = "LTS";
        public const string FFA = "FFA";

        public static readonly IReadOnlyList<string> All = new[] { TO, TDM, Arena, LTS, FFA };
    }

    [AddINotifyPropertyChangedInterface]
    public partial class BotConfigurationVM {
        public bool Enabled { get; set; }

        public int BotBackfillLowPlayers { get; set; } = 12;
        public int BotBackfillLowBots { get; set; } = 12;
        public int BotBackfillHighPlayers { get; set; } = 32;
        public int BotBackfillHighBots { get; set; } = 12;

        public static BotConfigurationVM FromMetadata(BotConfigurationMetadata? metadata) {
            if (metadata == null) return new BotConfigurationVM { Enabled = false };
            return new BotConfigurationVM {
                Enabled = true,
                BotBackfillLowPlayers = metadata.BotBackfillLowPlayers,
                BotBackfillLowBots = metadata.BotBackfillLowBots,
                BotBackfillHighPlayers = metadata.BotBackfillHighPlayers,
                BotBackfillHighBots = metadata.BotBackfillHighBots,
            };
        }

        public BotConfigurationMetadata? ToMetadata() {
            if (!Enabled) return null;
            return new BotConfigurationMetadata(
                BotBackfillLowPlayers,
                BotBackfillLowBots,
                BotBackfillHighPlayers,
                BotBackfillHighBots
            );
        }
    }

    [AddINotifyPropertyChangedInterface]
    public partial class NetworkConfigurationVM {
        public int NetServerMaxTickRate { get; set; } = 60;
        public int MaxClientRate { get; set; } = 100000;
        public int MaxInternetClientRate { get; set; } = 100000;
        public double InitialConnectTimeout { get; set; } = 60.0;
        public double ConnectionTimeout { get; set; } = 60.0;
        public int LanServerMaxTickRate { get; set; } = 60;
        public double RelevantTimeout { get; set; } = 5.0;
        public double SpawnPrioritySeconds { get; set; } = 1.0;
        public double ServerTravelPause { get; set; } = 4.0;

        public static NetworkConfigurationVM FromMetadata(NetworkConfigurationMetadata? metadata) {
            if (metadata == null) return new NetworkConfigurationVM();
            return new NetworkConfigurationVM {
                NetServerMaxTickRate = metadata.NetServerMaxTickRate,
                MaxClientRate = metadata.MaxClientRate,
                MaxInternetClientRate = metadata.MaxInternetClientRate,
                InitialConnectTimeout = metadata.InitialConnectTimeout,
                ConnectionTimeout = metadata.ConnectionTimeout,
                LanServerMaxTickRate = metadata.LanServerMaxTickRate,
                RelevantTimeout = metadata.RelevantTimeout,
                SpawnPrioritySeconds = metadata.SpawnPrioritySeconds,
                ServerTravelPause = metadata.ServerTravelPause,
            };
        }

        public NetworkConfigurationMetadata ToMetadata() => new NetworkConfigurationMetadata(
            NetServerMaxTickRate,
            MaxClientRate,
            MaxInternetClientRate,
            InitialConnectTimeout,
            ConnectionTimeout,
            LanServerMaxTickRate,
            RelevantTimeout,
            SpawnPrioritySeconds,
            ServerTravelPause
        );
    }

    [AddINotifyPropertyChangedInterface]
    public abstract partial class GameModeConfigurationVM {
        public abstract string Kind { get; }

        // Base configuration (applies to everything)
        public int FPS { get; set; } = 120;
        public int MaxPlayers { get; set; } = 64;
        public int IdleSpectateTimeout { get; set; } = 600;
        public int IdleKickTimeout { get; set; } = 900;
        public int MinimumWarmupTime { get; set; } = 20;
        public bool EnableHorses { get; set; } = true;
        public int MapChangePause { get; set; } = 10;
        public ObservableCollection<string> MapList { get; set; } = new();

        public BotConfigurationVM BotConfiguration { get; set; } = new();
        public NetworkConfigurationVM NetworkConfiguration { get; set; } = new();

        protected void LoadBase(GameModeConfigurationBaseMetadata metadata) {
            FPS = metadata.FPS;
            MaxPlayers = metadata.MaxPlayers;
            IdleSpectateTimeout = metadata.IdleSpectateTimeout;
            IdleKickTimeout = metadata.IdleKickTimeout;
            MinimumWarmupTime = metadata.MinimumWarmupTime;
            EnableHorses = metadata.EnableHorses;
            MapChangePause = metadata.MapChangePause;
            MapList = new ObservableCollection<string>(metadata.MapList ?? Array.Empty<string>());
            BotConfiguration = BotConfigurationVM.FromMetadata(metadata.BotConfiguration);
            NetworkConfiguration = NetworkConfigurationVM.FromMetadata(metadata.NetworkConfiguration);
        }

        protected GameModeConfigurationBaseMetadata ToBaseMetadata() => new GameModeConfigurationBaseMetadata(
            FPS,
            MaxPlayers,
            IdleSpectateTimeout,
            IdleKickTimeout,
            MinimumWarmupTime,
            EnableHorses,
            MapChangePause,
            MapList.ToArray(),
            BotConfiguration.ToMetadata(),
            NetworkConfiguration.ToMetadata()
        );

        public abstract GameModeConfigurationMetadata ToMetadata();

        public static GameModeConfigurationVM FromMetadata(GameModeConfigurationMetadata metadata) => metadata switch {
            FFAConfigurationMetadata m => new FFAConfigurationVM(m),
            TDMConfigurationMetadata m => new TDMConfigurationVM(m),
            TOConfigurationMetadata m => new TOConfigurationVM(m),
            LTSConfigurationMetadata m => new LTSConfigurationVM(m),
            ArenaConfigurationMetadata m => new ArenaConfigurationVM(m),
            _ => throw new ArgumentOutOfRangeException(nameof(metadata), metadata, "Unknown game mode metadata")
        };

        public static GameModeConfigurationVM CreateDefault(string kind) => kind switch {
            GameModeKinds.FFA => new FFAConfigurationVM(),
            GameModeKinds.TDM => new TDMConfigurationVM(),
            GameModeKinds.TO => new TOConfigurationVM(),
            GameModeKinds.LTS => new LTSConfigurationVM(),
            GameModeKinds.Arena => new ArenaConfigurationVM(),
            _ => new FFAConfigurationVM(),
        };
    }

    public partial class FFAConfigurationVM : GameModeConfigurationVM {
        public override string Kind => GameModeKinds.FFA;

        public FFAConfigurationVM() { }

        public FFAConfigurationVM(FFAConfigurationMetadata metadata) {
            LoadBase(metadata.Base);
        }

        public override GameModeConfigurationMetadata ToMetadata() =>
            new FFAConfigurationMetadata(ToBaseMetadata());
    }

    public partial class TDMConfigurationVM : GameModeConfigurationVM {
        public override string Kind => GameModeKinds.TDM;

        public TDMConfigurationVM() { }

        public TDMConfigurationVM(TDMConfigurationMetadata metadata) {
            LoadBase(metadata.Base);
        }

        public override GameModeConfigurationMetadata ToMetadata() =>
            new TDMConfigurationMetadata(ToBaseMetadata());
    }

    public partial class TOConfigurationVM : GameModeConfigurationVM {
        public override string Kind => GameModeKinds.TO;

        public TOConfigurationVM() { }

        public TOConfigurationVM(TOConfigurationMetadata metadata) {
            LoadBase(metadata.Base);
        }

        public override GameModeConfigurationMetadata ToMetadata() =>
            new TOConfigurationMetadata(ToBaseMetadata());
    }

    public partial class LTSConfigurationVM : GameModeConfigurationVM {
        public override string Kind => GameModeKinds.LTS;

        public int PreCountdownDelay { get; set; } = 5;
        public int RoundsToWin { get; set; } = 3;

        public LTSConfigurationVM() { }

        public LTSConfigurationVM(LTSConfigurationMetadata metadata) {
            LoadBase(metadata.Base);
            PreCountdownDelay = metadata.PreCountdownDelay;
            RoundsToWin = metadata.RoundsToWin;
        }

        public override GameModeConfigurationMetadata ToMetadata() =>
            new LTSConfigurationMetadata(ToBaseMetadata(), PreCountdownDelay, RoundsToWin);
    }

    public partial class ArenaConfigurationVM : GameModeConfigurationVM {
        public override string Kind => GameModeKinds.Arena;

        public int RoundsToWin { get; set; } = 3;
        public int RoundTimeLimit { get; set; } = 240;
        public bool ClearWeaponsBetweenRounds { get; set; } = true;
        public bool ClearHorsesBetweenRounds { get; set; } = true;
        public bool ResetActorsBetweenRounds { get; set; } = true;
        public bool UsePreCountdownForCustomizationLoading { get; set; } = true;
        public int MinTimeBeforeStartingMatch { get; set; } = 5;
        public int MaxTimeBeforeStartingMatch { get; set; } = 25;
        public int ExtraLivesPerPlayer { get; set; } = 0;

        public ArenaConfigurationVM() { }

        public ArenaConfigurationVM(ArenaConfigurationMetadata metadata) {
            LoadBase(metadata.Base);
            RoundsToWin = metadata.RoundsToWin;
            RoundTimeLimit = metadata.RoundTimeLimit;
            ClearWeaponsBetweenRounds = metadata.ClearWeaponsBetweenRounds;
            ClearHorsesBetweenRounds = metadata.ClearHorsesBetweenRounds;
            ResetActorsBetweenRounds = metadata.ResetActorsBetweenRounds;
            UsePreCountdownForCustomizationLoading = metadata.UsePreCountdownForCustomizationLoading;
            MinTimeBeforeStartingMatch = metadata.MinTimeBeforeStartingMatch;
            MaxTimeBeforeStartingMatch = metadata.MaxTimeBeforeStartingMatch;
            ExtraLivesPerPlayer = metadata.ExtraLivesPerPlayer;
        }

        public override GameModeConfigurationMetadata ToMetadata() =>
            new ArenaConfigurationMetadata(
                ToBaseMetadata(),
                RoundsToWin,
                RoundTimeLimit,
                ClearWeaponsBetweenRounds,
                ClearHorsesBetweenRounds,
                ResetActorsBetweenRounds,
                UsePreCountdownForCustomizationLoading,
                MinTimeBeforeStartingMatch,
                MaxTimeBeforeStartingMatch,
                ExtraLivesPerPlayer
            );
    }

    // --- JSON metadata (UnionTag/UnionCase) ---

    public record BotConfigurationMetadata(
        int BotBackfillLowPlayers,
        int BotBackfillLowBots,
        int BotBackfillHighPlayers,
        int BotBackfillHighBots);

    public record NetworkConfigurationMetadata(
        int NetServerMaxTickRate,
        int MaxClientRate,
        int MaxInternetClientRate,
        double InitialConnectTimeout,
        double ConnectionTimeout,
        int LanServerMaxTickRate,
        double RelevantTimeout,
        double SpawnPrioritySeconds,
        double ServerTravelPause);

    public record GameModeConfigurationBaseMetadata(
        int FPS,
        int MaxPlayers,
        int IdleSpectateTimeout,
        int IdleKickTimeout,
        int MinimumWarmupTime,
        bool EnableHorses,
        int MapChangePause,
        string[] MapList,
        BotConfigurationMetadata? BotConfiguration,
        NetworkConfigurationMetadata NetworkConfiguration);

    [UnionTag(nameof(Kind))]
    [UnionCase(typeof(FFAConfigurationMetadata), GameModeKinds.FFA)]
    [UnionCase(typeof(TDMConfigurationMetadata), GameModeKinds.TDM)]
    [UnionCase(typeof(TOConfigurationMetadata), GameModeKinds.TO)]
    [UnionCase(typeof(LTSConfigurationMetadata), GameModeKinds.LTS)]
    [UnionCase(typeof(ArenaConfigurationMetadata), GameModeKinds.Arena)]
    public abstract record GameModeConfigurationMetadata(string Kind);

    public record FFAConfigurationMetadata(GameModeConfigurationBaseMetadata Base)
        : GameModeConfigurationMetadata(GameModeKinds.FFA);

    public record TDMConfigurationMetadata(GameModeConfigurationBaseMetadata Base)
        : GameModeConfigurationMetadata(GameModeKinds.TDM);

    public record TOConfigurationMetadata(GameModeConfigurationBaseMetadata Base)
        : GameModeConfigurationMetadata(GameModeKinds.TO);

    public record LTSConfigurationMetadata(GameModeConfigurationBaseMetadata Base, int PreCountdownDelay, int RoundsToWin)
        : GameModeConfigurationMetadata(GameModeKinds.LTS);

    public record ArenaConfigurationMetadata(
        GameModeConfigurationBaseMetadata Base,
        int RoundsToWin,
        int RoundTimeLimit,
        bool ClearWeaponsBetweenRounds,
        bool ClearHorsesBetweenRounds,
        bool ResetActorsBetweenRounds,
        bool UsePreCountdownForCustomizationLoading,
        int MinTimeBeforeStartingMatch,
        int MaxTimeBeforeStartingMatch,
        int ExtraLivesPerPlayer)
        : GameModeConfigurationMetadata(GameModeKinds.Arena);
}
