using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using PropertyChanged;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab {

    public class ServerConfigurationCodec : DerivedJsonCodec<ObservableCollection<ServerConfiguration>,
        ObservableCollection<ServerConfigurationVM>> {

        public ServerConfigurationCodec(IModManager modManager) : base(
            ToJsonType,
            conf => ToClassType(conf, modManager)
        ) { }

        public static ObservableCollection<ServerConfigurationVM> ToClassType(
            ObservableCollection<ServerConfiguration> configurations, IModManager modManager) =>
            new ObservableCollection<ServerConfigurationVM>(configurations.Select(conf =>
                conf.Name == null // Should be impossible, but sometimes older dev builds have null names
                    ? new ServerConfigurationVM(modManager)
                    : new ServerConfigurationVM(
                        modManager,
                        conf.Name,
                        conf.Description,
                        conf.Password,
                        conf.LocalIp,
                        conf.GamePort,
                        conf.RconPort,
                        conf.A2SPort,
                        conf.PingPort,
                        conf.SelectedMap,
                        conf.ShowInServerBrowser,
                        conf.EnabledServerModList
                    )
            ));


        public static ObservableCollection<ServerConfiguration> ToJsonType(
            ObservableCollection<ServerConfigurationVM> configurations) =>
            new ObservableCollection<ServerConfiguration>(
                configurations.Select(conf => conf.ToServerConfiguration())
            );

    }

    public record ServerConfiguration(
        string Name = "My Server",
        string Description = "My Server Description",
        string Password = "",
        string? LocalIp = null,
        int GamePort = 7777,
        int RconPort = 9001,
        int A2SPort = 7071,
        int PingPort = 3075,
        string SelectedMap = "FFA_Courtyard",
        bool ShowInServerBrowser = true,
        ObservableCollection<ReleaseCoordinates>? EnabledServerModList = null) {

        public override string ToString() {
            var modListStr = EnabledServerModList == null
                ? "null"
                : string.Join(", ", EnabledServerModList.Select(mod => mod?.ToString() ?? "null"));
            return
                $"ServerConfiguration({Name}, {Description}, {Password}, {LocalIp}, {GamePort}, {RconPort}, {A2SPort}, {PingPort}, {SelectedMap}, {ShowInServerBrowser}, [{modListStr}])";
        }
    }

    [AddINotifyPropertyChangedInterface]
    public partial class ServerConfigurationVM : INotifyPropertyChanged {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Password { get; set; }
        public int GamePort { get; set; }
        public int RconPort { get; set; }
        public int A2SPort { get; set; }
        public int PingPort { get; set; }
        public string SelectedMap { get; set; }
        public bool ShowInServerBrowser { get; set; }
        public string LocalIp { get; set; }

        public ObservableCollection<string> AvailableMaps { get; }

        public ObservableCollection<ReleaseCoordinates> EnabledServerModList { get; }
        public ObservableCollection<Release> AvailableMods { get; }

        public ServerConfigurationVM(IModManager modManager,
            string name = "My Server",
            string description = "My Server Description",
            string password = "",
            string? localIp = null,
            int gamePort = 7777,
            int rconPort = 9001,
            int a2SPort = 7071,
            int pingPort = 3075,
            string selectedMap = "FFA_Courtyard",
            bool showInServerBrowser = true,
            ObservableCollection<ReleaseCoordinates>? enabledServerModList = null
        ) {
            Name = name;
            Description = description;
            Password = password;
            RconPort = rconPort;
            A2SPort = a2SPort;
            PingPort = pingPort;
            GamePort = gamePort;
            ShowInServerBrowser = showInServerBrowser;

            EnabledServerModList = enabledServerModList ?? new ObservableCollection<ReleaseCoordinates>();

            AvailableMaps = new ObservableCollection<string>(GetDefaultMaps());

            AvailableMods = new ObservableCollection<Release>();

            modManager.GetEnabledAndDependencyReleases()
                .Where(r => r.Manifest.ModType == ModType.Server || r.Manifest.ModType == ModType.Shared)
                .ForEach(x => AddAvailableMod(x, null));

            SelectedMap = selectedMap;

            LocalIp = localIp == null ? DetermineLocalIp() : localIp.Trim();

            modManager.ModDisabled += RemoveAvailableMod;
            modManager.ModEnabled += AddAvailableMod;
        }

        public void EnableServerMod(Release release) =>
            EnabledServerModList.Add(ReleaseCoordinates.FromRelease(release));

        public void DisableServerMod(Release release) =>
            EnabledServerModList.Remove(ReleaseCoordinates.FromRelease(release));

        public void AddAvailableMod(Release release, string? previousVersion) {
            // This will be enabled by default
            if (ModIdentifier.FromRelease(release) == CommonMods.UnchainedMods) return;

            var existingMod = AvailableMods.Find(x => x.Manifest.RepoUrl == release.Manifest.RepoUrl);
            var existingMaps = existingMod.Bind(x => Prelude.Optional(x.Manifest.Maps)).FirstOrDefault() ??
                               new List<string>();


            var newMaps = release.Manifest.Maps?.Filter(x => !existingMaps.Contains(x)) ?? Enumerable.Empty<string>();
            var removedMaps = existingMaps.Filter(x => !release.Manifest.Maps?.Contains(x) ?? false);

            removedMaps.ForEach(AvailableMaps.Remove);
            newMaps.ForEach(AvailableMaps.Add);

            existingMod.IfSome(x => AvailableMods.Remove(x));
            AvailableMods.Add(release);

        }

        public void RemoveAvailableMod(Release release) {
            AvailableMods.Remove(release);

            var removedMaps = release.Manifest.Maps;
            removedMaps.ForEach(AvailableMaps.Remove);
        }

        [RelayCommand]
        public void AutoFillIp() {
            LocalIp = DetermineLocalIp();
        }

        private string DetermineLocalIp() => GetAllLocalIPv4().FirstOrDefault("127.0.0.1");

        private static IEnumerable<string> GetDefaultMaps() {
            List<string> maps = new List<string>();
            using (var defaultMapsListStream = Assembly.GetExecutingAssembly()
                       .GetManifestResourceStream("UnchainedLauncher.GUI.Resources.DefaultMaps.txt")) {
                if (defaultMapsListStream != null) {
                    using var reader = new StreamReader(defaultMapsListStream);

                    var defaultMapsString = reader.ReadToEnd();
                    defaultMapsString
                        .Split("\n")
                        .Select(x => x.Trim())
                        .ToList()
                        .ForEach(maps.Add);
                }
            }

            return maps;
        }

        public static string[] GetAllLocalIPv4() =>
            NetworkInterface
                .GetAllNetworkInterfaces()
                .Where(x => x.OperationalStatus == OperationalStatus.Up)
                .SelectMany(x => x.GetIPProperties().UnicastAddresses)
                .Where(ip => ip.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(x => x.Address.ToString())
                .ToArray();

        public ServerConfiguration ToServerConfiguration() => new ServerConfiguration(Name, Description, Password,
            LocalIp, GamePort, RconPort, A2SPort, PingPort, SelectedMap, ShowInServerBrowser, EnabledServerModList);

        public override string ToString() {
            var enabledMods = EnabledServerModList != null
                ? string.Join(", ", EnabledServerModList.Select(mod => mod?.ToString() ?? "null"))
                : "null";
            return
                $"ServerConfigurationVM({Name}, {Description}, {Password}, {LocalIp}, {GamePort}, {RconPort}, {A2SPort}, {PingPort}, {SelectedMap}, {ShowInServerBrowser}, [{enabledMods}])";
        }
    }

    public record BotConfiguration(
        int BotBackfillLowPlayers,
        int BotBackfillLowBots,
        int BotBackfillHighPlayers,
        int BotBackfillHighBots) {
        public IReadOnlyList<CLIArg> GetCLIArgs() => new List<CLIArg> {
            new UEINIParameter("Game", "/Script/TBL.TBLGameMode", "BotBackfillLowPlayers",
                BotBackfillLowPlayers.ToString()),
            new UEINIParameter("Game", "/Script/TBL.TBLGameMode", "BotBackfillLowBots", BotBackfillLowBots.ToString()),
            new UEINIParameter("Game", "/Script/TBL.TBLGameMode", "BotBackfillHighPlayers",
                BotBackfillHighPlayers.ToString()),
            new UEINIParameter("Game", "/Script/TBL.TBLGameMode", "BotBackfillHighBots", BotBackfillHighBots.ToString())
        };
    }

    public readonly record struct NetworkConfiguration(
        int NetServerMaxTickRate = 60,
        int MaxClientRate = 100000,
        int MaxInternetClientRate = 100000,
        double InitialConnectTimeout = 60.0,
        double ConnectionTimeout = 60.0,
        int LanServerMaxTickRate = 60,
        double RelevantTimeout = 5.0,
        double SpawnPrioritySeconds = 1.0,
        double ServerTravelPause = 4.0
    ) {
        private const string IpNetDriverSection = "/Script/OnlineSubsystemUtils.IpNetDriver";

        public IReadOnlyList<CLIArg> GetCLIArgs() {
            return new List<CLIArg> {
                new UEINIParameter("Engine", IpNetDriverSection, "NetServerMaxTickRate", NetServerMaxTickRate.ToString()),
                new UEINIParameter("Engine", IpNetDriverSection, "MaxClientRate", MaxClientRate.ToString()),
                new UEINIParameter("Engine", IpNetDriverSection, "MaxInternetClientRate", MaxInternetClientRate.ToString()),
                new UEINIParameter("Engine", IpNetDriverSection, "InitialConnectTimeout", InitialConnectTimeout.ToString()),
                new UEINIParameter("Engine", IpNetDriverSection, "ConnectionTimeout", ConnectionTimeout.ToString()),
                new UEINIParameter("Engine", IpNetDriverSection, "LanServerMaxTickRate", LanServerMaxTickRate.ToString()),
                new UEINIParameter("Engine", IpNetDriverSection, "RelevantTimeout", RelevantTimeout.ToString()),
                new UEINIParameter("Engine", IpNetDriverSection, "SpawnPrioritySeconds", SpawnPrioritySeconds.ToString()),
                new UEINIParameter("Engine", IpNetDriverSection, "ServerTravelPause", ServerTravelPause.ToString()),
            };
        }
    }

    public abstract record GameModeConfiguration(
        int FPS,
        int MaxPlayers,
        int IdleSpectateTimeout,
        int IdleKickTimeout,
        int MinimumWarmupTime,
        bool EnableHorses,
        int MapChangePause,
        IReadOnlyList<string> MapList,
        Option<BotConfiguration> BotConfiguration,
        NetworkConfiguration NetworkConfiguration,
        GameModeTypeConfig GameModeConfig) {
        public abstract IReadOnlyList<CLIArg> GetCLIArgs();

        protected IReadOnlyList<CLIArg> GetBaseConfiguration() {
            var args = new List<CLIArg> {
                new UEINIParameter("Game", "/Script/Engine.GameSession", "MaxPlayers", MaxPlayers.ToString()),
                new UEINIParameter("GameUserSettings", "/Script/TBL.TBLGameUserSettings", "MaxFPS", FPS.ToString()),
                new UEINIParameter("GameUserSettings", "/Script/TBL.TBLGameUserSettings", "FrameRateLimit",
                    FPS.ToString()),
                new UEINIParameter("Game", "/Script/TBL.TBLGameMode", "IdleKickTimerSpectate", IdleSpectateTimeout.ToString()),
                new UEINIParameter("Game", "/Script/TBL.TBLGameMode", "IdleKickTimerDisconnect", IdleKickTimeout.ToString()),
                new UEINIParameter("Game", "/Script/TBL.TBLGameMode", "MinTimeBeforeStartingMatch", MinimumWarmupTime.ToString()),
                new UEINIParameter("Game", "/Script/TBL.TBLGameMode", "bHorseCompatibleServer", EnableHorses.ToString()),
            };
            args.AddRange(GetMapList());
            args.AddRange(GetBotConfiguration());
            args.AddRange(NetworkConfiguration.GetCLIArgs());
            args.AddRange(GameModeConfig.GetCLIArgs());
            return args;
        }

        protected IReadOnlyList<CLIArg> GetBotConfiguration() =>
            BotConfiguration.Match(
                bot =>
                    new UEINIParameter("Game", "/Script/TBL.TBLGameMode", "BotBackfillEnabled", "True")
                        .Cons(bot.GetCLIArgs())
                        .ToList(),
                () => [new UEINIParameter("Game", "/Script/TBL.TBLGameMode", "BotBackfillEnabled", "False")]
            );

        protected IReadOnlyList<CLIArg> GetMapList() {
            var maps = MapList.Select(map => new UEINIParameter("Game", "/Script/TBL.TBLGameMode", "+Maplist", map));
            return new UEINIParameter("Game", "/Script/TBL.TBLGameMode", "MapListIndex", "-1")
                .Cons(maps)
                .ToList();
        }
    }

    public abstract record GameModeTypeConfig() {
        public abstract IReadOnlyList<CLIArg> GetCLIArgs();
    }

    public record FFAConfig() : GameModeTypeConfig() {
        public override IReadOnlyList<CLIArg> GetCLIArgs() => new List<CLIArg>();
    }

    // Auto balance options are applicable for the range of players (inclusive) between MinNumPlayers and MaxNumPlayers, allowing AllowedNumPlayersDifference between team size
    public record AutoBalanceOption(int MinNumPlayers, int MaxNumPlayers, int AllowedNumPlayersDifference);

    public record TeamGameModeConfig(
        int AutobalanceGracePeriod,
        int TeamSwitchingGracePeriod,
        bool StrictTeamBalanceEnforcement,
        IReadOnlyList<AutoBalanceOption> TeamBalanceOptions,
        IReadOnlyList<AutoBalanceOption> AutoBalanceOptions,
        TeamGameModeTypeConfig SpecificModeConfig) : GameModeTypeConfig() {
        public override IReadOnlyList<CLIArg> GetCLIArgs() {
            var args = new List<CLIArg> {
                new UEINIParameter("Game", "/Script/TBL.TBLGameMode", "StartOfMatchGracePeriodForAutoBalance",
                    AutobalanceGracePeriod.ToString()),
                new UEINIParameter("Game", "/Script/TBL.TBLGameMode", "StartOfMatchGracePeriodForTeamSwitching",
                    TeamSwitchingGracePeriod.ToString()),
                new UEINIParameter("Game", "/Script/TBL.TBLGameMode", "bUseStrictTeamBalanceEnforcement",
                    StrictTeamBalanceEnforcement.ToString()),
            };

            var teamBalanceOptions = TeamBalanceOptions.Select(option =>
                new UEINIParameter("Game", "/Script/TBL.TBLGameMode", "+TeamBalanceOptions",
                    $"(MinNumPlayers={option.MinNumPlayers},MaxNumPlayers={option.MaxNumPlayers},AllowedNumPlayersDifference={option.AllowedNumPlayersDifference})"));
            args.AddRange(teamBalanceOptions);

            var autoBalanceOptions = AutoBalanceOptions.Select(option =>
                new UEINIParameter("Game", "/Script/TBL.TBLGameMode", "+AutoBalanceOptions",
                    $"(MinNumPlayers={option.MinNumPlayers},MaxNumPlayers={option.MaxNumPlayers},AllowedNumPlayersDifference={option.AllowedNumPlayersDifference})"));
            args.AddRange(autoBalanceOptions);

            args.AddRange(SpecificModeConfig.GetCLIArgs());
            return args;
        }
    }

    public abstract record TeamGameModeTypeConfig() {
        public abstract IReadOnlyList<CLIArg> GetCLIArgs();
    };

    public record TDMConfig() : TeamGameModeTypeConfig {
        public override IReadOnlyList<CLIArg> GetCLIArgs() => new List<CLIArg>();
    }; // TDM has no specific args

    public record LTSConfig(int PreCountdownDelay, int RoundsToWin) : TeamGameModeTypeConfig {
        public override IReadOnlyList<CLIArg> GetCLIArgs() {
            var rounds = (RoundsToWin * 2) - 1;
            return new List<CLIArg> {
                new UEINIParameter("Game", "/Script/TBL.LTSGameMode", "PreCountdownDelay", PreCountdownDelay.ToString()),
                new UEINIParameter("Game", "/Script/TBL.LTSGameMode", "Rounds", rounds.ToString()),
            };
        }
    }

    public record TOConfig() : TeamGameModeTypeConfig {
        public override IReadOnlyList<CLIArg> GetCLIArgs() => new List<CLIArg>();
    }

    public record ArenaConfig(
        int RoundsToWin,
        int RoundTimeLimit,
        bool ClearWeaponsBetweenRounds,
        bool ClearHorsesBetweenRounds,
        bool ResetActorsBetweenRounds,
        bool UsePreCountdownForCustomizationLoading,
        int MinTimeBeforeStartingMatch,
        int MaxTimeBeforeStartingMatch,
        int ExtraLivesPerPlayer) : TeamGameModeTypeConfig {
        public override IReadOnlyList<CLIArg> GetCLIArgs() {
            var rounds = (RoundsToWin * 2) - 1;
            return new List<CLIArg> {
                new UEINIParameter("Game", "/Script/TBL.ArenaGameMode", "Rounds", rounds.ToString()),
                new UEINIParameter("Game", "/Script/TBL.ArenaGameMode", "RoundTimeLimit", RoundTimeLimit.ToString()),
                new UEINIParameter("Game", "/Script/TBL.ArenaGameMode", "bClearWeaponsPostRound", ClearWeaponsBetweenRounds.ToString()),
                new UEINIParameter("Game", "/Script/TBL.ArenaGameMode", "bClearHorsesPostRound", ClearHorsesBetweenRounds.ToString()),
                new UEINIParameter("Game", "/Script/TBL.ArenaGameMode", "bResetTaggedActorsPostRound", ResetActorsBetweenRounds.ToString()),
                new UEINIParameter("Game", "/Script/TBL.ArenaGameMode", "bUsePreCountdownForCustomizationLoading", UsePreCountdownForCustomizationLoading.ToString()),
                new UEINIParameter("Game", "/Script/TBL.ArenaGameMode", "MinTimeBeforeStartingMatch", MinTimeBeforeStartingMatch.ToString()),
                new UEINIParameter("Game", "/Script/TBL.ArenaGameMode", "MaxTimeBeforeStartingMatch", MaxTimeBeforeStartingMatch.ToString()),
                new UEINIParameter("Game", "/Script/TBL.ArenaGameMode", "TeamLives", ExtraLivesPerPlayer.ToString()),
            };
        }
    }

}