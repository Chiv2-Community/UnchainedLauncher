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

namespace C2GUILauncher.ViewModels {
    [AddINotifyPropertyChangedInterface]
    public class ServerLauncherViewModel {
        private static readonly ILog logger = LogManager.GetLogger(nameof(ServerLauncherViewModel));
        private static readonly string SettingsFilePath = $"{FilePaths.ModCachePath}\\unchained_launcher_server_settings.json";

        public string ServerName { get; set; }
        public string ServerDescription { get; set; }
        public short GamePort { get; set; }
        public short RconPort { get; set; }
        public short A2sPort { get; set; }
        public short PingPort { get; set; }
        public string SelectedMap { get; set; }
        public bool ShowInServerBrowser { get; set; }
        public bool CanClick { get; set; }

        public ObservableCollection<string> MapsList { get; set; }
        private LauncherViewModel LauncherViewModel { get; }
        private SettingsViewModel SettingsViewModel { get; }
        public ICommand LaunchServerCommand { get; }
        public ICommand LaunchServerHeadlessCommand { get; }


        public FileBackedSettings<ServerSettings> SettingsFile { get; set; }

        private ModManager ModManager { get; }
        //may want to add a mods list here as well,
        //in the hopes of having multiple independent servers running one one machine
        //whose settings can be stored/loaded from files

        public ServerLauncherViewModel(LauncherViewModel launcherViewModel, SettingsViewModel settingsViewModel, ModManager modManager, string serverName, string serverDescription, string selectedMap, short gamePort, short rconPort, short a2sPort, short pingPort, bool showInServerBrowser, FileBackedSettings<ServerSettings> settingsFile) {
            CanClick = true;
            
            ServerName = serverName;
            ServerDescription = serverDescription;
            SelectedMap = selectedMap;
            GamePort = gamePort;
            RconPort = rconPort;
            A2sPort = a2sPort;
            PingPort = pingPort;
            ShowInServerBrowser = showInServerBrowser;
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
                "FFA_Courtyard",
                7777,
                9001,
                7071,
                3075,
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
                loadedSettings.SelectedMap ?? defaultSettings.SelectedMap!,
                loadedSettings.GamePort ?? defaultSettings.GamePort.Value,
                loadedSettings.RconPort ?? defaultSettings.RconPort.Value,
                loadedSettings.A2sPort ?? defaultSettings.A2sPort.Value,
                loadedSettings.PingPort ?? defaultSettings.PingPort.Value,
                loadedSettings.ShowInServerBrowser ?? defaultSettings.ShowInServerBrowser.Value,
                fileBackedSettings
            );
            #pragma warning restore CS8629 // Nullable value type may be null.
        }

        public void SaveSettings() {
            SettingsFile.SaveSettings(
                new ServerSettings(
                    ServerName, 
                    ServerDescription, 
                    SelectedMap,
                    GamePort, 
                    RconPort, 
                    A2sPort, 
                    PingPort,
                    ShowInServerBrowser
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

                string[] exArgs = { 
                    $"Port={GamePort}", 
                    $"GameServerQueryPort={PingPort}", 
                    $"GameServerQueryPort={A2sPort}",
                    $"-rcon {RconPort}",
                };

                await LauncherViewModel.LaunchModded(exArgs, serverRegister);

            } catch (Exception ex) {
                MessageBox.Show("Failed to launch. Check the logs for details.");
                logger.Error("Failed to launch.", ex);
                CanClick = true;
            }

        }

        private async void LaunchServerHeadless() {
            CanClick = false;

            Process? serverRegister = await MakeRegistrationProcess();
            if (serverRegister == null) {
                return;
            }

            try {
                string[] exArgs = {
                    $"Port={GamePort}", //specify server port
                    $"GameServerQueryPort={PingPort}",
                    $"GameServerQueryPort={A2sPort}",
                    "-nullrhi", //disable rendering
                    //Note the distinction here with the other ports.
                    //The rcon flag DOES NOT support the equals sign syntax
                    $"-rcon {RconPort}", //let the serverplugin know that we want RCON running on the given port
                    "-RenderOffScreen", //super-disable rendering
                    "-unattended", //let it know no one's around to help
                    "-nosound", //disable sound
                    "--next-map-name " + SelectedMap
                };

                await LauncherViewModel.LaunchModded(exArgs, serverRegister);
            } catch (Exception ex) {
                MessageBox.Show(ex.ToString());
                CanClick = true;
            }
        }

        private async Task<Process?> MakeRegistrationProcess() {
            if (!File.Exists("RegisterUnchainedServer.exe")) {
                DownloadTask serverRegisterDownload = HttpHelpers.DownloadFileAsync(
                    "https://github.com/Chiv2-Community/C2ServerAPI/releases/latest/download/RegisterUnchainedServer.exe",
                    "RegisterUnchainedServer.exe"
                );

                try {
                    await serverRegisterDownload.Task;
                } catch (Exception e) {
                    MessageBox.Show("Failed to download the Unchained server registration program:\n" + e.Message);
                    return null;
                }
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
                $"-g \"{GamePort}\" ";

            if (!ShowInServerBrowser) {
                registerCommand += " --no-register";
            }

            logger.Info($"Running registration: {registerCommand}");

            serverRegister.StartInfo.Arguments = $"/C \"{registerCommand}\"";
            serverRegister.StartInfo.CreateNoWindow = false;

            return serverRegister;
        }
    }
}
