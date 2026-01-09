using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using log4net;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.Core.INIModels;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.GUI.ViewModels.ServersTab.IniSections;
using UnchainedLauncher.GUI.ViewModels.ServersTab.Sections;

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
                        conf.ShowInServerBrowser,
                        conf.FFAScoreLimit,
                        conf.FFATimeLimit,
                        conf.TDMTicketCount,
                        conf.TDMTimeLimit,
                        conf.PlayerBotCount,
                        conf.WarmupTime,
                        conf.DiscordBotToken,
                        conf.DiscordChannelId,
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
        string NextMapName = "FFA_Courtyard",
        bool ShowInServerBrowser = true,
        int? FFAScoreLimit = null,
        int? FFATimeLimit = null,
        int? TDMTicketCount = null,
        int? TDMTimeLimit = null,
        int? PlayerBotCount = null,
        int? WarmupTime = null,
        string DiscordBotToken = "",
        string DiscordChannelId = "",
        ObservableCollection<ReleaseCoordinates>? EnabledServerModList = null) {

        public string SavedDirSuffix => ServerConfigurationVM.SavedDirSuffix(Name);

        public override string ToString() {
            var modListStr = EnabledServerModList == null
                ? "null"
                : string.Join(", ", EnabledServerModList.Select(mod => mod?.ToString() ?? "null"));
            return
                $"ServerConfiguration({Name}, {Description}, {Password}, {LocalIp}, {GamePort}, {RconPort}, {A2SPort}, {PingPort}, {NextMapName}, {ShowInServerBrowser}, [{modListStr}])";
        }
    }

    [AddINotifyPropertyChangedInterface]
    public partial class ServerConfigurationVM : INotifyPropertyChanged {
        private readonly ILog Logger = LogManager.GetLogger(nameof(ServerConfigurationVM));

        public IpNetDriverSectionVM IpNetDriver { get; } = new();
        public GameSessionSectionVM GameSession { get; } = new();
        public TBLGameModeSectionVM GameMode { get; } = new();
        public LtsGameModeSectionVM LTS { get; } = new();
        public ArenaGameModeSectionVM Arena { get; } = new();
        public TBLGameUserSettingsSectionVM UserSettings { get; } = new();
        public FfaConfigurationSectionVM FFA { get; private set; }
        public TdmConfigurationSectionVM TDM { get; private set; }


        public BaseConfigurationSectionVM BaseConfigurationSection { get; }
        public AdvancedConfigurationSectionVM AdvancedConfigurationSection { get; }

        public string Name {
            get;
            set {
                if (string.IsNullOrWhiteSpace(Name)) {
                    field = value;
                    return;
                }

                var oldSuffix = SavedDirSuffix(Name);

                field = value.Trim();
                var newSuffix = SavedDirSuffix(Name);

                if (Directory.Exists(FilePaths.Chiv2ConfigPath(oldSuffix))) {
                    if (Directory.Exists(FilePaths.Chiv2ConfigPath(newSuffix))) {
                        // Replace the old one
                        Directory.Delete(FilePaths.Chiv2ConfigPath(newSuffix), true);
                    }

                    Directory.CreateDirectory(Directory.GetParent(FilePaths.Chiv2ConfigPath(newSuffix))!.FullName);
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
        public bool ShowInServerBrowser { get; set; }
        public string LocalIp { get; set; }
        
        public string DiscordBotToken { get; set; }
        public string DiscordChannelId { get; set; }
        
        public int? FFATimeLimit { get; set; }
        public int? FFAScoreLimit { get; set; }

        public int? TDMTimeLimit { get; set; }
        public int? TDMTicketCount { get; set; }

        public int? PlayerBotCount { get; set; }
        public int? WarmupTime { get; set; }

        public static string SavedDirSuffix(string name) {
            var substitutedUnderscores = name.Trim()
            .Replace(' ', '_')
            .Replace('(', '_')
            .Replace(')', '_')
            .ReplaceLineEndings("_");

            var illegalCharsRemoved = string.Join("_", substitutedUnderscores.Split(Path.GetInvalidFileNameChars()));
            return illegalCharsRemoved;
        }

        public void LoadINI(string? name) {
            var ini = Chivalry2INI.LoadINIProfile(SavedDirSuffix(name ?? Name));

            IpNetDriver.LoadFrom(ini.Engine.IpNetDriver);
            GameSession.LoadFrom(ini.Game.GameSession);

            GameMode.DefaultMaxPlayers = GameSession.MaxPlayers;
            GameMode.LoadFrom(ini.Game.TBLGameMode);

            LTS.LoadFrom(ini.Game.LTSGameMode);
            Arena.LoadFrom(ini.Game.ArenaGameMode);
            UserSettings.LoadFrom(ini.GameUserSettings.TBLGameUserSettings);
        }

        public bool SaveINI() {
            var ini = ToChivalry2INI();
            return ini.SaveINIProfile(SavedDirSuffix(Name));
        }

        private Chivalry2INI ToChivalry2INI() {
            var engineIni = new EngineINI(IpNetDriver.ToModel());
            var gameIni = new GameINI(
                GameSession.ToModel(),
                GameMode.ToModel(),
                LTS.ToModel(),
                Arena.ToModel()
            );

            var userSettingsIni = new GameUserSettingsINI(UserSettings.ToModel());
            return new Chivalry2INI(engineIni, gameIni, userSettingsIni);
        }

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
            bool showInServerBrowser = true,
            int? ffaScoreLimit = null,
            int? ffaTimeLimit = null,
            int? tdmTicketCount = null,
            int? tdmTimeLimit = null,
            int? playerBotCount = null,
            int? warmupTime = null,
            string discordBotToken = "",
            string discordChannelId = "",
            ObservableCollection<ReleaseCoordinates>? enabledServerModList = null
        ) {
            Description = description;
            Password = password;
            RconPort = rconPort;
            A2SPort = a2SPort;
            PingPort = pingPort;
            GamePort = gamePort;
            ShowInServerBrowser = showInServerBrowser;
            
            DiscordBotToken = discordBotToken;
            DiscordChannelId = discordChannelId;

            FFAScoreLimit = ffaScoreLimit;
            FFATimeLimit = ffaTimeLimit;
            TDMTicketCount = tdmTicketCount;
            TDMTimeLimit = tdmTimeLimit;
            PlayerBotCount = playerBotCount;
            WarmupTime = warmupTime;

            EnabledServerModList = enabledServerModList ?? new ObservableCollection<ReleaseCoordinates>();

            AvailableMaps = new ObservableCollection<string>(GetDefaultMaps());

            // We set the Name after loading INI, because there may be some existing config that we want to load first
            // And setting the name overwrites it.
            LoadINI(name);
            Name = name;

            AvailableMods = new ObservableCollection<Release>();

            modManager.GetEnabledAndDependencyReleases()
                .Where(r => r.Manifest.ModType is ModType.Server or ModType.Shared)
                .ForEach(x => AddAvailableMod(x, null));

            LocalIp = localIp == null ? DetermineLocalIp() : localIp.Trim();

            modManager.ModDisabled += RemoveAvailableMod;
            modManager.ModEnabled += AddAvailableMod;

            BaseConfigurationSection = new BaseConfigurationSectionVM(this);
            AdvancedConfigurationSection = new AdvancedConfigurationSectionVM(this);

            TDM = new TdmConfigurationSectionVM(this);
            FFA = new FfaConfigurationSectionVM(this);
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

            var removedMaps = release.Manifest.Maps ?? Enumerable.Empty<string>();
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

        public ServerConfiguration ToServerConfiguration() => new ServerConfiguration(
            Name,
            Description,
            Password,
            LocalIp,
            GamePort,
            RconPort,
            A2SPort,
            PingPort,
            DetermineNextMapName(),
            ShowInServerBrowser,
            FFAScoreLimit,
            FFATimeLimit,
            TDMTicketCount,
            TDMTimeLimit,
            PlayerBotCount,
            WarmupTime,
            DiscordBotToken,
            DiscordChannelId,
            EnabledServerModList
        );

        private string DetermineNextMapName() {
            // Prefer the selected rotation entry. If rotation is empty, fall back to a safe default.
            if (GameMode.MapList.Count == 0) return "FFA_Courtyard";

            var idx = GameMode.MapListIndex;
            if (idx < 0 || idx >= GameMode.MapList.Count) {
                idx = 0;
            }

            return GameMode.MapList[idx];
        }

        public override string ToString() {
            var enabledMods = EnabledServerModList != null
                ? string.Join(", ", EnabledServerModList.Select(mod => mod?.ToString() ?? "null"))
                : "null";
            return
                $"ServerConfigurationVM({Name}, {Description}, {Password}, {LocalIp}, {GamePort}, {RconPort}, {A2SPort}, {PingPort}, {DetermineNextMapName()}, {ShowInServerBrowser}, [{enabledMods}])";
        }


        [RelayCommand]
        private void OpenIniFolder() {
            try {
                var iniDir = FilePaths.Chiv2ConfigPath(SavedDirSuffix(Name));
                Directory.CreateDirectory(iniDir);

                Process.Start(new ProcessStartInfo {
                    FileName = iniDir,
                    UseShellExecute = true
                });
            }
            catch (Exception ex) {
                Logger.Warn("Failed to open INI folder", ex);
            }
        }

        [RelayCommand]
        private void ReloadIni() {
            LoadINI(Name);
        }
    }
}