using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using log4net;
using log4net.Core;
using log4net.Repository.Hierarchy;
using PropertyChanged;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using StructuredINI.Codecs;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.Core.INIModels;
using UnchainedLauncher.Core.INIModels.Engine;
using UnchainedLauncher.Core.INIModels.Game;
using UnchainedLauncher.Core.INIModels.GameUserSettings;
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
        private readonly ILog Logger = LogManager.GetLogger(nameof(ServerConfigurationVM));

        public class AutoBalanceVM {
            public int MinNumPlayers { get; set; }
            public int MaxNumPlayers { get; set; }
            public int AllowedNumPlayersDifference { get; set; }

            public AutoBalanceVM() { }

            public AutoBalanceVM(AutoBalance ab) {
                MinNumPlayers = ab.MinNumPlayers;
                MaxNumPlayers = ab.MaxNumPlayers;
                AllowedNumPlayersDifference = ab.AllowedNumPlayersDifference;
            }

            public AutoBalance ToModel() => new AutoBalance(MinNumPlayers, MaxNumPlayers, AllowedNumPlayersDifference);
        }

        private bool _syncingFps;
        public string Name {
            get;
            set {
                if (string.IsNullOrWhiteSpace(Name)) {
                    field = value;
                    return;
                }

                var oldSuffix = SavedDirSuffix();
                
                field = value.Trim();
                var newSuffix = SavedDirSuffix();
                
                if (Directory.Exists(FilePaths.Chiv2ConfigPath(oldSuffix))) {
                    Directory.Move(FilePaths.Chiv2ConfigPath(oldSuffix), FilePaths.Chiv2ConfigPath(newSuffix));
                }
            }
        }

        public string Description { get; set; }
        public string Password { get; set; }
        public int GamePort { get; set; }
        public int RconPort { get; set; }
        public int A2SPort { get; set; }
        public int PingPort { get; set; }
        public string SelectedMap { get; set; }
        public bool ShowInServerBrowser { get; set; }
        public string LocalIp { get; set; }

        // Engine.ini -> [/Script/OnlineSubsystemUtils.IpNetDriver]
        public int NetServerMaxTickRate { get; set; }
        public int MaxClientRate { get; set; }
        public int MaxInternetClientRate { get; set; }
        public float InitialConnectTimeout { get; set; }
        public float ConnectionTimeout { get; set; }
        public int LanServerMaxTickRate { get; set; }
        public float RelevantTimeout { get; set; }
        public int SpawnPrioritySeconds { get; set; }
        public float ServerTravelPause { get; set; }

        // Game.ini -> [/Script/Engine.GameSession]
        public int MaxPlayers { get; set; }

        // Game.ini -> [/Script/TBL.TBLGameMode]
        public string IniServerName { get; set; }
        public string ServerIdentifier { get; set; }
        public bool BotBackfillEnabled { get; set; }
        public int BotBackfillLowPlayers { get; set; }
        public int BotBackfillLowBots { get; set; }
        public int BotBackfillHighPlayers { get; set; }
        public int BotBackfillHighBots { get; set; }
        public float MinTimeBeforeStartingMatch { get; set; }
        public int IdleKickTimerSpectate { get; set; }
        public int IdleKickTimerDisconnect { get; set; }
        public ObservableCollection<string> MapList { get; set; }
        public string? MapToAdd { get; set; }
        public int MapListIndex { get; set; }
        public bool HorseCompatibleServer { get; set; }
        public ObservableCollection<AutoBalanceVM> TeamBalanceOptions { get; set; }
        public ObservableCollection<AutoBalanceVM> AutoBalanceOptions { get; set; }
        public int StartOfMatchGracePeriodForAutoBalance { get; set; }
        public int StartOfMatchGracePeriodForTeamSwitching { get; set; }
        public bool UseStrictTeamBalanceEnforcement { get; set; }

        public bool AutoBalanceEnabled {
            get => AutoBalanceOptions.Count > 0;
            set {
                if (value) {
                    if (AutoBalanceOptions.Count == 0) {
                        AutoBalanceOptions.Add(new AutoBalanceVM(new AutoBalance(0, MaxPlayers, 1)));
                    }
                }
                else {
                    AutoBalanceOptions.Clear();
                }

                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AutoBalanceEnabled)));
            }
        }

        public bool TeamBalanceEnabled {
            get => TeamBalanceOptions.Count > 0;
            set {
                if (value) {
                    if (TeamBalanceOptions.Count == 0) {
                        TeamBalanceOptions.Add(new AutoBalanceVM(new AutoBalance(0, MaxPlayers, 1)));
                    }
                }
                else {
                    TeamBalanceOptions.Clear();
                }

                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TeamBalanceEnabled)));
            }
        }

        // Game.ini -> [/Script/TBL.LTSGameMode]
        public int LtsPreCountdownDelay { get; set; }
        public int LtsRoundsToWin { get; set; }

        // Game.ini -> [/Script/TBL.ArenaGameMode]
        public int ArenaRoundsToWin { get; set; }
        public int ArenaRoundTimeLimit { get; set; }
        public bool ArenaClearWeaponsPostRound { get; set; }
        public bool ArenaClearHorsesPostRound { get; set; }
        public bool ArenaResetTaggedActorsPostRound { get; set; }
        public bool ArenaUsePreCountdownForCustomizationLoading { get; set; }
        public int ArenaMinTimeBeforeStartingMatch { get; set; }
        public int ArenaMaxTimeBeforeStartingMatch { get; set; }
        public int ArenaTeamLives { get; set; }

        // GameUserSettings.ini -> [/Script/TBL.TBLGameUserSettings]
        public int MaxFPS { get; set; }
        public float FrameRateLimit { get; set; }

        public int FpsLimit {
            get => MaxFPS;
            set {
                _syncingFps = true;
                MaxFPS = value;
                FrameRateLimit = value;
                _syncingFps = false;
            }
        }

        private void OnMaxFPSChanged() {
            if (_syncingFps) return;
            _syncingFps = true;
            FrameRateLimit = MaxFPS;
            _syncingFps = false;
        }

        private void OnFrameRateLimitChanged() {
            if (_syncingFps) return;
            _syncingFps = true;
            MaxFPS = (int)FrameRateLimit;
            _syncingFps = false;
        }

        private static int ToRoundsToWin(int rounds) {
            return (rounds + 1) / 2;
        }

        private static int FromRoundsToWin(int roundsToWin) {
            if (roundsToWin < 1) roundsToWin = 1;
            return (2 * roundsToWin) - 1;
        }

        public string SavedDirSuffix() {
            var substitutedUnderscores = Name.Trim()
            .Replace(' ', '_')
            .Replace('(', '_')
            .Replace(')', '_')
            .ReplaceLineEndings("_");

            var illegalCharsRemoved = string.Join("_", substitutedUnderscores.Split(Path.GetInvalidFileNameChars()));
            return illegalCharsRemoved;
        }

        public void LoadINI() {
            var ini = Chivalry2INI.LoadINIProfile(SavedDirSuffix());

            var ipNetDriver = ini.Engine.IpNetDriver;
            
            NetServerMaxTickRate = ipNetDriver.NetServerMaxTickRate;
            MaxClientRate = ipNetDriver.MaxClientRate;
            MaxInternetClientRate = ipNetDriver.MaxInternetClientRate;
            InitialConnectTimeout = ipNetDriver.InitialConnectTimeout;
            ConnectionTimeout = ipNetDriver.ConnectionTimeout;
            LanServerMaxTickRate = ipNetDriver.LanServerMaxTickRate;
            RelevantTimeout = ipNetDriver.RelevantTimeout;
            SpawnPrioritySeconds = ipNetDriver.SpawnPrioritySeconds;
            ServerTravelPause = ipNetDriver.ServerTravelPause;

            var gameSession = ini.Game.GameSession;
            MaxPlayers = gameSession.MaxPlayers;

            var tblGameMode = ini.Game.TBLGameMode;
            IniServerName = tblGameMode.ServerName;
            ServerIdentifier = tblGameMode.ServerIdentifier;
            BotBackfillEnabled = tblGameMode.BotBackfillEnabled;
            BotBackfillLowPlayers = tblGameMode.BotBackfillLowPlayers;
            BotBackfillLowBots = tblGameMode.BotBackfillLowBots;
            BotBackfillHighPlayers = tblGameMode.BotBackfillHighPlayers;
            BotBackfillHighBots = tblGameMode.BotBackfillHighBots;
            MinTimeBeforeStartingMatch = tblGameMode.MinTimeBeforeStartingMatch;
            IdleKickTimerSpectate = tblGameMode.IdleKickTimerSpectate;
            IdleKickTimerDisconnect = tblGameMode.IdleKickTimerDisconnect;

            MapList.Clear();
            foreach (var map in tblGameMode.MapList) {
                MapList.Add(map);
            }

            MapListIndex = tblGameMode.MapListIndex;
            HorseCompatibleServer = tblGameMode.bHorseCompatibleServer;

            TeamBalanceOptions.Clear();
            foreach (var opt in tblGameMode.TeamBalanceOptions) {
                TeamBalanceOptions.Add(new AutoBalanceVM(opt));
            }

            AutoBalanceOptions.Clear();
            foreach (var opt in tblGameMode.AutoBalanceOptions) {
                AutoBalanceOptions.Add(new AutoBalanceVM(opt));
            }

            StartOfMatchGracePeriodForAutoBalance = tblGameMode.StartOfMatchGracePeriodForAutoBalance;
            StartOfMatchGracePeriodForTeamSwitching = tblGameMode.StartOfMatchGracePeriodForTeamSwitching;
            UseStrictTeamBalanceEnforcement = tblGameMode.bUseStrictTeamBalanceEnforcement;

            var lts = ini.Game.LTSGameMode;
            LtsPreCountdownDelay = lts.PreCountdownDelay;
            LtsRoundsToWin = ToRoundsToWin(lts.Rounds);

            var arena = ini.Game.ArenaGameMode;
            ArenaRoundsToWin = ToRoundsToWin(arena.Rounds);
            ArenaRoundTimeLimit = arena.RoundTimeLimit;
            ArenaClearWeaponsPostRound = arena.bClearWeaponsPostRound;
            ArenaClearHorsesPostRound = arena.bClearHorsesPostRound;
            ArenaResetTaggedActorsPostRound = arena.bResetTaggedActorsPostRound;
            ArenaUsePreCountdownForCustomizationLoading = arena.bUsePreCountdownForCustomizationLoading;
            ArenaMinTimeBeforeStartingMatch = arena.MinTimeBeforeStartingMatch;
            ArenaMaxTimeBeforeStartingMatch = arena.MaxTimeBeforeStartingMatch;
            ArenaTeamLives = arena.TeamLives;

            var userSettings = ini.GameUserSettings.TBLGameUserSettings;
            MaxFPS = userSettings.MaxFPS;
            FrameRateLimit = userSettings.FrameRateLimit;
        }

        public bool SaveINI() {
            var ini = ToChivalry2INI();
            return ini.SaveINIProfile(SavedDirSuffix());
        }

        private Chivalry2INI ToChivalry2INI() {
            var engineIni = new EngineINI(new IpNetDriver(
                NetServerMaxTickRate,
                MaxClientRate,
                MaxInternetClientRate,
                InitialConnectTimeout,
                ConnectionTimeout,
                LanServerMaxTickRate,
                RelevantTimeout,
                SpawnPrioritySeconds,
                ServerTravelPause
            ));

            var gameIni = new GameINI(
                new GameSession(MaxPlayers),
                new TBLGameMode(
                    IniServerName,
                    ServerIdentifier,
                    BotBackfillEnabled,
                    BotBackfillLowPlayers,
                    BotBackfillLowBots,
                    BotBackfillHighPlayers,
                    BotBackfillHighBots,
                    MinTimeBeforeStartingMatch,
                    IdleKickTimerSpectate,
                    IdleKickTimerDisconnect,
                    MapList.ToArray(),
                    MapListIndex,
                    HorseCompatibleServer,
                    TeamBalanceOptions.Select(o => o.ToModel()).ToArray(),
                    AutoBalanceOptions.Select(o => o.ToModel()).ToArray(),
                    StartOfMatchGracePeriodForAutoBalance,
                    StartOfMatchGracePeriodForTeamSwitching,
                    UseStrictTeamBalanceEnforcement
                ),
                new LTSGameMode(LtsPreCountdownDelay, FromRoundsToWin(LtsRoundsToWin)),
                new ArenaGameMode(
                    FromRoundsToWin(ArenaRoundsToWin),
                    ArenaRoundTimeLimit,
                    ArenaClearWeaponsPostRound,
                    ArenaClearHorsesPostRound,
                    ArenaResetTaggedActorsPostRound,
                    ArenaUsePreCountdownForCustomizationLoading,
                    ArenaMinTimeBeforeStartingMatch,
                    ArenaMaxTimeBeforeStartingMatch,
                    ArenaTeamLives
                )
            );

            var userSettingsIni = new GameUserSettingsINI(new TBLGameUserSettings(MaxFPS, FrameRateLimit));
            return new Chivalry2INI(engineIni, gameIni, userSettingsIni);
        }

        public ObservableCollection<string> AvailableMaps { get; }

        public ObservableCollection<ReleaseCoordinates> EnabledServerModList { get; }
        public ObservableCollection<Release> AvailableMods { get; }

        public IRelayCommand AddMapCommand { get; }
        public IRelayCommand<string> RemoveMapCommand { get; }

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

            MapList = new ObservableCollection<string>();
            AutoBalanceOptions = new ObservableCollection<AutoBalanceVM>();
            TeamBalanceOptions = new ObservableCollection<AutoBalanceVM>();
            
            LoadINI();

            AvailableMods = new ObservableCollection<Release>();

            modManager.GetEnabledAndDependencyReleases()
                .Where(r => r.Manifest.ModType == ModType.Server || r.Manifest.ModType == ModType.Shared)
                .ForEach(x => AddAvailableMod(x, null));

            SelectedMap = selectedMap;

            LocalIp = localIp == null ? DetermineLocalIp() : localIp.Trim();

            modManager.ModDisabled += RemoveAvailableMod;
            modManager.ModEnabled += AddAvailableMod;
        }

        public void ApplyINI() {
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
}