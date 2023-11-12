using C2GUILauncher.JsonModels;
using C2GUILauncher.Mods;
using PropertyChanged;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Linq;
using C2GUILauncher.JsonModels.Metadata.V3;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Input;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using log4net.Repository.Hierarchy;
using log4net;
using C2GUILauncher.Views;
using Semver;

namespace C2GUILauncher.ViewModels {
    [AddINotifyPropertyChangedInterface]
    public class ServerLauncherViewModel {
        private static readonly ILog logger = LogManager.GetLogger(nameof(ServerLauncherViewModel));
        private static readonly string SettingsFilePath = $"{FilePaths.ModCachePath}\\unchained_launcher_server_settings.json";

        public string ServerName { get; set; }
        public string ServerDescription { get; set; }
        public string ServerPassword { get; set; }
        public short GamePort { get; set; }
        public short RconPort { get; set; }
        public short A2sPort { get; set; }
        public short PingPort { get; set; }
        public string SelectedMap { get; set; }
        public bool ShowInServerBrowser { get; set; }
        public bool UseBackendBanList { get; set; }
        public bool CanClick { get; set; }

        public ObservableCollection<string> MapsList { get; set; }
        private LauncherViewModel LauncherViewModel { get; }
        private SettingsViewModel SettingsViewModel { get; }
        public ICommand LaunchServerCommand { get; }
        public ICommand LaunchServerHeadlessCommand { get; }

        public static GithubReleaseSynchronizer RegisterServerSynchronizer = 
            new GithubReleaseSynchronizer(
                "Chiv2-Community",
                "C2ServerAPI",
                "RegisterUnchainedServer.exe",
                "RegisterUnchainedServer.exe"
            );

        public FileBackedSettings<ServerSettings> SettingsFile { get; set; }

        private ModManager ModManager { get; }
        //may want to add a mods list here as well,
        //in the hopes of having multiple independent servers running one one machine
        //whose settings can be stored/loaded from files

        public ServerLauncherViewModel(LauncherViewModel launcherViewModel, SettingsViewModel settingsViewModel, ModManager modManager, string serverName, string serverDescription, string serverPassword, string selectedMap, short gamePort, short rconPort, short a2sPort, short pingPort, bool showInServerBrowser, bool useBackendBanList, FileBackedSettings<ServerSettings> settingsFile) {
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
            UseBackendBanList = useBackendBanList;

            SettingsViewModel = settingsViewModel;

            SettingsFile = settingsFile;

            LauncherViewModel = launcherViewModel;
            ModManager = modManager;

            LaunchServerCommand = new RelayCommand(LaunchServer);
            LaunchServerHeadlessCommand = new RelayCommand(LaunchServerHeadless);

            MapsList = new ObservableCollection<string>();

            using (Stream? defaultMapsListStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("C2GUILauncher.DefaultMaps.txt")) {
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
        }

        private void ProcessEnabledModsChanged(object? sender, NotifyCollectionChangedEventArgs e) {
            if(e.OldItems != null) {
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

        public static ServerLauncherViewModel LoadSettings(LauncherViewModel launcherViewModel, SettingsViewModel settingsViewModel, ModManager modManager) {
            var defaultSettings = new ServerSettings(
                "Chivalry 2 server",
                "Example description",
                "",
                "FFA_Courtyard",
                7777,
                9001,
                7071,
                3075,
                true,
                true
            );

            var fileBackedSettings = new FileBackedSettings<ServerSettings>(SettingsFilePath, defaultSettings);

            var loadedSettings = fileBackedSettings.LoadSettings();


            #pragma warning disable CS8629 // Every call to .Value is safe here because all default server settings are defined.
            return new ServerLauncherViewModel(
                launcherViewModel,
                settingsViewModel,
                modManager,
                loadedSettings.ServerName ?? defaultSettings.ServerName!,
                loadedSettings.ServerDescription ?? defaultSettings.ServerDescription!,
                loadedSettings.ServerPassword ?? defaultSettings.ServerPassword!,
                loadedSettings.SelectedMap ?? defaultSettings.SelectedMap!,
                loadedSettings.GamePort ?? defaultSettings.GamePort.Value,
                loadedSettings.RconPort ?? defaultSettings.RconPort.Value,
                loadedSettings.A2sPort ?? defaultSettings.A2sPort.Value,
                loadedSettings.PingPort ?? defaultSettings.PingPort.Value,
                loadedSettings.ShowInServerBrowser ?? defaultSettings.ShowInServerBrowser.Value,
                loadedSettings.UseBackendBanList ?? defaultSettings.UseBackendBanList.Value,
                fileBackedSettings
            );
            #pragma warning restore CS8629 // Nullable value type may be null.
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
                    ShowInServerBrowser,
                    UseBackendBanList
                )
            );
        }

        private async void LaunchServer() {
            CanClick = false;
            try {
                Process? serverRegister = await MakeRegistrationProcess();
                if (serverRegister == null) {
                    return;
                }

                var exArgs = new List<string>(){ 
                    $"Port={GamePort}", 
                    $"GameServerPingPort={PingPort}", 
                    $"GameServerQueryPort={A2sPort}",
                    $"-rcon {RconPort}",
                };

                if(UseBackendBanList) {
                    exArgs.Add("--use-backend-banlist");
                }

                if(ServerPassword.Trim() != "") {
                    exArgs.Add($"ServerPassword={ServerPassword.Trim()}");
                }

                await LauncherViewModel.LaunchModded(exArgs, serverRegister);

            } catch (Exception ex) {
                MessageBox.Show("Failed to launch. Check the logs for details.");
                logger.Error("Failed to launch.", ex);
            }
            CanClick = true;
        }

        private async void LaunchServerHeadless() {
            CanClick = false;

            Process? serverRegister = await MakeRegistrationProcess();
            if (serverRegister == null) {
                CanClick = true;
                return;
            }

            try {
                var exArgs = new List<string>(){
                    $"Port={GamePort}", //specify server port
                    $"GameServerPingPort={PingPort}",
                    $"GameServerQueryPort={A2sPort}",
                    "-nullrhi", //disable rendering
                    //Note the distinction here with the other ports.
                    //The rcon flag DOES NOT support the equals sign syntax
                    $"-rcon {RconPort}", //let the serverplugin know that we want RCON running on the given port
                    "-nosound", //disable sound
                    "--next-map-name " + SelectedMap
                };

                if (UseBackendBanList) {
                    exArgs.Add("--use-backend-banlist");
                }

                if (ServerPassword.Trim() != "") {
                    exArgs.Add($"ServerPassword={ServerPassword.Trim()}");
                }

                await LauncherViewModel.LaunchModded(exArgs, serverRegister);
            } catch (Exception ex) {
                MessageBox.Show(ex.ToString());
            }
            CanClick = true;
        }

        private List<string> MakeModArgsList() {
            var modArgs = new List<string>();

            foreach (Release modRelease in ModManager.EnabledModReleases) {
                modArgs.Add($"--mod {modRelease.Manifest.OrgName}/{modRelease.Manifest.Name}={modRelease.Tag}");
            }

            return modArgs;
        }

        private async Task<Process?> MakeRegistrationProcess() {
            var keepGoing = await DownloadRegistrationProcess();

            if(!keepGoing) {
                return null;
            }

            Process serverRegister = new Process();
            //We *must* use cmd.exe as a wrapper to start RegisterUnchainedServer.exe, otherwise we have no way to
            //close the window later

            //TODO: Get this to actually be able to be closed
            serverRegister.StartInfo.FileName = "cmd.exe";

            string registerCommand = $"RegisterUnchainedServer.exe " +
                $"-n \"{ServerName.Replace("\"", "\"")}\" " +
                $"-d \"{ServerDescription.Replace("\"", "\"").Replace("\n", "\n")}\" " +
                $"-r \"{SettingsViewModel.ServerBrowserBackend}\" " +
                $"-c \"{RconPort}\" " +
                $"-a \"{A2sPort}\" " +
                $"-p \"{PingPort}\" " +
                $"-g \"{GamePort}\" " + 
                string.Join(" ", MakeModArgsList().Aggregate((x, y) => x + " " + y));

            if (!ShowInServerBrowser) {
                registerCommand += "--no-register ";
            }

            if(ServerPassword.Trim() != "") {
                registerCommand += $"-x ";
            }

            logger.Info($"Running registration: {registerCommand}");

            serverRegister.StartInfo.Arguments = $"/C \"{registerCommand}\"";
            serverRegister.StartInfo.CreateNoWindow = false;

            return serverRegister;
        }

        private async Task<bool> DownloadRegistrationProcess() {
            var installed = File.Exists(RegisterServerSynchronizer.OutputPath);
            var upToDate = false;
            string? updateVersion = null;
            SemVersion? currentVersion = RegisterServerSynchronizer.GetCurrentVersion();

            if (currentVersion != null)
            {
                var updateCheck = await RegisterServerSynchronizer.CheckForUpdates(currentVersion);

                updateCheck.MatchVoid(
                    failed: () => logger.Warn("Failed to check for RegisterUnchainedServer.exe updates."),
                    upToDate: () => {
                        logger.Info("RegisterUnchainedServer.exe is up to date.");
                        upToDate = true;
                    },
                    available: tag => {
                        logger.Info("RegisterUnchainedServer.exe update available: " + tag);
                        updateVersion = tag;
                    }
                );
            } else {
                updateVersion = await RegisterServerSynchronizer.GetLatestTag();
            }

            if (upToDate) 
                return true;

            if (updateVersion == null) {
                logger.Error("Failed to get latest version url");
                MessageBox.Show("Failed to check for RegisterUnchainedServer.exe updates");
                return installed;
            }

            var titlePrefix = installed
                ? "Install"
                : "Update";

            var message = installed
                ? "The server registration process is out of date. Would you like to update it?"
                : "The server registration process is not installed. Would you like to install it?";

            var result = UpdatesWindow.Show($"{titlePrefix} registration process?", message, "Yes", "No", null, new List<DependencyUpdate>() {
                    new DependencyUpdate("RegisterUnchainedServer.exe", installed ? currentVersion?.ToString() ?? "Unknown" : null, updateVersion, RegisterServerSynchronizer.ReleaseUrl(updateVersion), "Required to register your server to the server browser")
            });

            if(result == MessageBoxResult.No) {
                logger.Info("User declined to install registration process");
                return installed;
            }

            try {
                await RegisterServerSynchronizer.DownloadRelease(updateVersion).Task;
                return true;
            } catch (Exception e) {
                MessageBox.Show("Failed to download the Unchained server registration program:\n" + e.Message);
                return false;
            }
        }
    }
}
