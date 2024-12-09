using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using log4net;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using UnchainedLauncher.Core;
using UnchainedLauncher.Core.API;
using UnchainedLauncher.Core.API.ServerBrowser;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Mods;
using UnchainedLauncher.Core.Mods.Registry;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.GUI.JsonModels;

namespace UnchainedLauncher.GUI.ViewModels {
    public partial class ServerLauncherViewModel : IDisposable, INotifyPropertyChanged {
        private static readonly ILog logger = LogManager.GetLogger(nameof(ServerLauncherViewModel));
        private static readonly string SettingsFilePath = $"{FilePaths.ModCachePath}\\unchained_launcher_server_settings.json";

        public string ServerName { get; set; }
        public string ServerDescription { get; set; }
        public string ServerPassword { get; set; }
        public int GamePort { get; set; }
        public int RconPort { get; set; }
        public int A2sPort { get; set; }
        public int PingPort { get; set; }
        public string SelectedMap { get; set; }
        public bool ShowInServerBrowser { get; set; }
        public bool CanClick { get; set; }

        public ObservableCollection<string> MapsList { get; set; }
        private LauncherViewModel LauncherViewModel { get; }
        private SettingsViewModel SettingsViewModel { get; }
        private ServersViewModel ServersViewModel { get; }
        public string ButtonToolTip { get; set; }
        public ICommand LaunchServerCommand { get; }
        public ICommand LaunchServerHeadlessCommand { get; }


        public FileBackedSettings<ServerSettings> SettingsFile { get; set; }

        private ModManager ModManager { get; }
        //may want to add a mods list here as well,
        //in the hopes of having multiple independent servers running one one machine
        //whose settings can be stored/loaded from files

        public ServerLauncherViewModel(LauncherViewModel launcherViewModel, SettingsViewModel settingsViewModel, ServersViewModel serversViewModel, ModManager modManager, string serverName, string serverDescription, string serverPassword, string selectedMap, int gamePort, int rconPort, int a2sPort, int pingPort, bool showInServerBrowser, FileBackedSettings<ServerSettings> settingsFile) {
            CanClick = true;

            ServerName = serverName;
            ServerDescription = serverDescription;
            ServerPassword = serverPassword;

            SelectedMap = selectedMap;
            GamePort = gamePort;
            RconPort = rconPort;
            A2sPort = a2sPort;
            PingPort = pingPort;
            ShowInServerBrowser = showInServerBrowser;
            SettingsViewModel = settingsViewModel;

            SettingsFile = settingsFile;

            LauncherViewModel = launcherViewModel;
            ServersViewModel = serversViewModel;
            ModManager = modManager;

            ButtonToolTip = "";

            LaunchServerCommand = new RelayCommand(() => RunServerLaunch(false));
            LaunchServerHeadlessCommand = new RelayCommand(() => RunServerLaunch(true));

            MapsList = new ObservableCollection<string>();

            using (Stream? defaultMapsListStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("UnchainedLauncher.GUI.Resources.DefaultMaps.txt")) {
                if (defaultMapsListStream != null) {
                    using StreamReader reader = new StreamReader(defaultMapsListStream!);

                    var defaultMapsString = reader.ReadToEnd();
                    defaultMapsString
                        .Split("\n")
                        .Select(x => x.Trim())
                        .ToList()
                        .ForEach(MapsList.Add);
                }
            }

            ModManager.EnabledModReleases.SelectMany(x => x.Manifest.Maps).ToList().ForEach(MapsList.Add);

            ModManager.EnabledModReleases.CollectionChanged += ProcessEnabledModsChanged;

            LauncherViewModel.PropertyChanged += (_, args) => {
                switch (args.PropertyName) {
                    case nameof(LauncherViewModel.CanClick):
                        CanClick = LauncherViewModel.CanClick;
                        break;
                    case nameof(LauncherViewModel.ButtonToolTip):
                        ButtonToolTip = LauncherViewModel.ButtonToolTip;
                        break;
                }
            };
        }

        private void ProcessEnabledModsChanged(object? sender, NotifyCollectionChangedEventArgs e) {
            if (e.OldItems != null) {
                foreach (Release modRelease in e.OldItems) {
                    modRelease.Manifest.Maps.ForEach(x => MapsList.Remove(x));
                }
            }

            if (e.NewItems != null) {
                foreach (Release modRelease in e.NewItems) {
                    modRelease.Manifest.Maps.ForEach(MapsList.Add);
                }
            }
        }

        public static ServerLauncherViewModel LoadSettings(LauncherViewModel launcherViewModel, SettingsViewModel settingsViewModel, ServersViewModel serversViewModel, ModManager modManager) {
            var fileBackedSettings = new FileBackedSettings<ServerSettings>(SettingsFilePath);
            var loadedSettings = fileBackedSettings.LoadSettings();

            return new ServerLauncherViewModel(
                launcherViewModel,
                settingsViewModel,
                serversViewModel,
                modManager,
                loadedSettings?.ServerName ?? "Chivalry 2 server",
                loadedSettings?.ServerDescription ?? "",
                loadedSettings?.ServerPassword ?? "",
                loadedSettings?.SelectedMap ?? "FFA_Courtyard",
                loadedSettings?.GamePort ?? 7777,
                loadedSettings?.RconPort ?? 9001,
                loadedSettings?.A2sPort ?? 7071,
                loadedSettings?.PingPort ?? 3075,
                loadedSettings?.ShowInServerBrowser ?? true,
                fileBackedSettings
            );
        }

        public void SaveSettings() {
            SettingsFile.SaveSettings(
                new ServerSettings(
                    ServerName,
                    ServerDescription,
                    ServerPassword,
                    SelectedMap,
                    GamePort,
                    RconPort,
                    A2sPort,
                    PingPort,
                    ShowInServerBrowser
                )
            );
        }

        private void RunServerLaunch(bool headless) {
            CanClick = false;
            try {
                var serverLaunchOptions = new ServerLaunchOptions(
                    headless,
                    ServerName,
                    ServerDescription,
                    Prelude.Some(ServerPassword).Map(pw => pw.Trim()).Filter(pw => pw != ""),
                    SelectedMap,
                    GamePort,
                    PingPort,
                    A2sPort,
                    RconPort
                );

                var maybeProcess = LauncherViewModel.LaunchUnchained(Prelude.Some(serverLaunchOptions));
                CanClick = LauncherViewModel.CanClick;

                maybeProcess.IfSome(process => {
                    var ports = new PublicPorts(
                        serverLaunchOptions.GamePort,
                        serverLaunchOptions.BeaconPort,
                        serverLaunchOptions.QueryPort
                    );
                    var serverInfo = new C2ServerInfo() {
                        Ports = ports,
                        Name = serverLaunchOptions.Name,
                        Description = serverLaunchOptions.Description,
                        PasswordProtected = serverLaunchOptions.Password.IsSome,
                        Mods = ModManager.EnabledModReleases.Select(release =>
                            new ServerBrowserMod(
                                release.Manifest.Name,
                                release.Version.ToString(),
                                release.Manifest.Organization
                            )
                        ).ToArray()
                    };

                    ServersViewModel.RegisterServer("127.0.0.1", serverLaunchOptions.RconPort, serverInfo, process);
                });
            }
            catch (Exception ex) {
                MessageBox.Show("Failed to launch. Check the logs for details.");
                logger.Error("Failed to launch.", ex);
                CanClick = LauncherViewModel.CanClick;
            }
        }

        public void Dispose() {
            SaveSettings();
            GC.SuppressFinalize(this);
        }
    }
}