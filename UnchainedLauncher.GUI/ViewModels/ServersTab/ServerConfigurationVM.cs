using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using log4net;
using Newtonsoft.Json;
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
using System.Text;
using System.Text.Json;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.Core.INIModels;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.GUI.ViewModels.ServersTab.IniSections;
using UnchainedLauncher.GUI.ViewModels.ServersTab.Sections;
using UnchainedLauncher.UnrealModScanner.GUI.ViewModels;
using UnchainedLauncher.UnrealModScanner.JsonModels;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnchainedLauncher.UnrealModScanner.Services;
using JsonSerializer = Newtonsoft.Json.JsonSerializer;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab {

    public class ServerConfigurationCodec : DerivedJsonCodec<ObservableCollection<ServerConfiguration>,
        ObservableCollection<ServerConfigurationVM>> {

        public ServerConfigurationCodec(
            IModManager modManager, 
            ModScanTabVM modScanTab,
            AvailableModsAndMapsVM availableModsAndMaps) : base(
            ToJsonType,
            conf => ToClassType(conf, modManager, modScanTab, availableModsAndMaps)
        ) { }

        public static ObservableCollection<ServerConfigurationVM> ToClassType(
            ObservableCollection<ServerConfiguration> configurations, 
            IModManager modManager, 
            ModScanTabVM modScanTab,
            AvailableModsAndMapsVM availableModsAndMaps) =>
            new ObservableCollection<ServerConfigurationVM>(configurations.Select(conf =>
                conf.Name == null // Should be impossible, but sometimes older dev builds have null names
                    ? new ServerConfigurationVM(modManager, modScanTab, availableModsAndMaps)
                    : new ServerConfigurationVM(
                        modManager,
                        modScanTab,
                        availableModsAndMaps,
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
        string NextMapPath = "FFA_Courtyard",
        bool ShowInServerBrowser = true,
        int? FFAScoreLimit = null,
        int? FFATimeLimit = null,
        int? TDMTicketCount = null,
        int? TDMTimeLimit = null,
        int? PlayerBotCount = null,
        int? WarmupTime = null,
        ObservableCollection<BlueprintDto>? EnabledServerModList = null) {
        
        public string SavedDirSuffix => ServerConfigurationVM.SavedDirSuffix(Name);

        public override string ToString() {
            var modListStr = EnabledServerModList == null
                ? "null"
                : string.Join(", ", EnabledServerModList.Select(mod => mod?.ToString() ?? "null"));
            return
                $"ServerConfiguration({Name}, {Description}, {Password}, {LocalIp}, {GamePort}, {RconPort}, {A2SPort}, {PingPort}, {NextMapPath}, {ShowInServerBrowser}, [{modListStr}])";
        }
    }

    [AddINotifyPropertyChangedInterface]
    public partial class ServerConfigurationVM : INotifyPropertyChanged {
        private static readonly ILog Logger = LogManager.GetLogger(nameof(ServerConfigurationVM));

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
        public BalanceSectionVM BalanceSection { get; }

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
        public string LocalIp { get; set; }

        public static string SavedDirSuffix(string name) {
            var validChars = "_0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
            var sb = new StringBuilder();
            name.ForEach(c => sb.Append(validChars.Contains(c) ? c : '_'));
            return sb.ToString();
        }

        public void LoadINI(string? name) {
            var ini = Chivalry2INI.LoadINIProfile(SavedDirSuffix(name ?? Name));

            IpNetDriver.LoadFrom(ini.Engine.IpNetDriver);
            GameSession.LoadFrom(ini.Game.GameSession);

            GameMode.DefaultMaxPlayers = GameSession.MaxPlayers;
            GameMode.LoadFrom(ini.Game.TBLGameMode, AvailableMaps);

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
                Arena.ToModel(),
                null,
                null
            );

            var userSettingsIni = new GameUserSettingsINI(UserSettings.ToModel());
            return new Chivalry2INI(engineIni, gameIni, userSettingsIni);
        }

        public ObservableCollection<MapDto> AvailableMaps => _availableModsAndMaps.AvailableMaps;

        public ObservableCollection<BlueprintDto> EnabledServerModList { get; }
        public ObservableCollection<BlueprintDto> AvailableServerModBlueprints => _availableModsAndMaps.AvailableServerModBlueprints;

        private ModScanTabVM _modScanTab;
        private AvailableModsAndMapsVM _availableModsAndMaps;

        public ServerConfigurationVM(
            IModManager modManager,
            ModScanTabVM modScanTab,
            AvailableModsAndMapsVM availableModsAndMaps,
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
            ObservableCollection<BlueprintDto>? enabledServerModList = null
        ) {
            _modScanTab = modScanTab;
            _availableModsAndMaps = availableModsAndMaps;
            Description = description;
            Password = password;
            RconPort = rconPort;
            A2SPort = a2SPort;
            PingPort = pingPort;
            GamePort = gamePort;

            EnabledServerModList = enabledServerModList ?? new ObservableCollection<BlueprintDto>();

            // We set the Name after loading INI, because there may be some existing config that we want to load first
            // And setting the name overwrites it.
            LoadINI(name);
            Name = name;


            LocalIp = localIp == null ? DetermineLocalIp() : localIp.Trim();


            BaseConfigurationSection = new BaseConfigurationSectionVM(
                GameMode,
                UserSettings,
                GameSession,
                AvailableMaps
            );

            AdvancedConfigurationSection = new AdvancedConfigurationSectionVM(
                IpNetDriver,
                GameMode,
                showInServerBrowser,
                playerBotCount,
                warmupTime
            );

            BalanceSection = new BalanceSectionVM(GameMode);

            TDM = new TdmConfigurationSectionVM(tdmTimeLimit, tdmTicketCount);
            FFA = new FfaConfigurationSectionVM(ffaTimeLimit, ffaScoreLimit);

        }

        public void EnableServerBlueprintMod(BlueprintDto blueprint) =>
            EnabledServerModList.Add(blueprint);

        public void DisableServerBlueprintMod(BlueprintDto blueprint) =>
            EnabledServerModList.Remove(blueprint);

        [RelayCommand]
        public void AutoFillIp() {
            LocalIp = DetermineLocalIp();
        }

        private string DetermineLocalIp() => GetAllLocalIPv4().FirstOrDefault("127.0.0.1");

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
            DetermineNextMap()!.TravelToMapString(),
            AdvancedConfigurationSection.ShowInServerBrowser,
            FFA.FFAScoreLimit,
            FFA.FFATimeLimit,
            TDM.TDMTicketCount,
            TDM.TDMTimeLimit,
            AdvancedConfigurationSection.PlayerBotCount,
            AdvancedConfigurationSection.WarmupTime,
            EnabledServerModList
        );

        private MapDto? DetermineNextMap() {
            // Prefer the selected rotation entry. If rotation is empty, fall back to a safe default.
            if (GameMode.MapList.Count == 0) return AvailableMaps.FirstOrDefault();

            var idx = GameMode.MapListIndex;
            if (idx < 0 || idx >= GameMode.MapList.Count) {
                idx = 0;
            }

            var selectedMap = GameMode.MapList[idx];

            return selectedMap;
        }
        
        public override string ToString() {
            var enabledMods = string.Join(", ", EnabledServerModList.Select(mod => mod?.ToString() ?? "null"));
            return
                $"ServerConfigurationVM({Name}, {Description}, {Password}, {LocalIp}, {GamePort}, {RconPort}, {A2SPort}, {PingPort}, {DetermineNextMap()}, {AdvancedConfigurationSection.ShowInServerBrowser}, [{enabledMods}])";
        }


        [RelayCommand]
        private void OpenIniFolder() {
            try {
                var iniDir = FilePaths.Chiv2ConfigPath(SavedDirSuffix(Name));
                SaveINI();

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