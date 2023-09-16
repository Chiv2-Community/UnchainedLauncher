using C2GUILauncher.JsonModels;
using C2GUILauncher.Mods;
using PropertyChanged;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Linq;
using C2GUILauncher.JsonModels.Metadata.V2;
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
        private static ILog logger = LogManager.GetLogger(nameof(ServerLauncherViewModel));
        private static readonly string SettingsFilePath = $"{FilePaths.ModCachePath}\\unchained_launcher_server_settings.json";

        public string ServerName { get; set; }
        public string ServerDescription { get; set; }
        public string ServerList { get; set; }
        public int GamePort { get; set; }
        public int RconPort { get; set; }
        public int A2sPort { get; set; }
        public int PingPort { get; set; }
        public string SelectedMap { get; set; } = "FFA_Courtyard";
        public ObservableCollection<string> MapsList { get; set; }
        private LauncherViewModel LauncherViewModel { get; }
        public ICommand LaunchServerCommand { get; }
        public ICommand LaunchServerHeadlessCommand { get; }


        public FileBackedSettings<ServerSettings> SettingsFile { get; set; }

        private ModManager ModManager { get; }
        //may want to add a mods list here as well,
        //in the hopes of having multiple independent servers running one one machine
        //whose settings can be stored/loaded from files

        public ServerLauncherViewModel(LauncherViewModel launcherViewModel, ModManager modManager, string serverName, string serverDescription, string serverList, int gamePort, int rconPort, int a2sPort, int pingPort, FileBackedSettings<ServerSettings> settingsFile) {
            ServerName = serverName;
            ServerDescription = serverDescription;
            ServerList = serverList;
            GamePort = gamePort;
            RconPort = rconPort;
            A2sPort = a2sPort;
            PingPort = pingPort;

            SettingsFile = settingsFile;

            LauncherViewModel = launcherViewModel;
            ModManager = modManager;

            LaunchServerCommand = new RelayCommand(LaunchServer);
            LaunchServerHeadlessCommand = new RelayCommand(LaunchServerHeadless);

            using Stream? defaultMapsListStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("C2GUILauncher.DefaultMaps.txt");
            using StreamReader reader = new StreamReader(defaultMapsListStream!);

            MapsList = new ObservableCollection<string>();
            if (defaultMapsListStream != null) {
                var defaultMapsString = reader.ReadToEnd();
                defaultMapsString
                    .Split("\n")
                    .Select(x => x.Trim())
                    .ToList()
                    .ForEach(MapsList.Add);
            }

            ModManager.EnabledModReleases
                .SelectMany(modRelease => modRelease.Manifest.Maps)
                .Distinct()
                .ToList()
                .ForEach(MapsList.Add);

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

        public static ServerLauncherViewModel LoadSettings(LauncherViewModel launcherViewModel, ModManager modManager) {
            var defaultSettings = new ServerSettings(
                "Chivalry 2 server",
                "Example description",
                "https://servers.polehammer.net",
                7777,
                9001,
                7071,
                3075
            );

            var fileBackedSettings = new FileBackedSettings<ServerSettings>(SettingsFilePath, defaultSettings);

            var loadedSettings = fileBackedSettings.LoadSettings();

            return new ServerLauncherViewModel(
                launcherViewModel,
                modManager,
                loadedSettings.ServerName,
                loadedSettings.ServerDescription,
                loadedSettings.ServerList,
                loadedSettings.GamePort,
                loadedSettings.RconPort,
                loadedSettings.A2sPort,
                loadedSettings.PingPort,
                fileBackedSettings
            );
        }

        public void SaveSettings() {
            SettingsFile.SaveSettings(
                new ServerSettings(
                    ServerName, 
                    ServerDescription, 
                    ServerList, 
                    GamePort, 
                    RconPort, 
                    A2sPort, 
                    PingPort
                )
            );
        }

        private async void LaunchServer() {
            try {
                Process? serverRegister = await MakeRegistrationProcess();
                if (serverRegister == null) {
                    return;
                }

                string loaderMap = "agmods?map=frontend" + LauncherViewModel.BuildModsString() + "?listen";
                string[] exArgs = { $"-port {GamePort}" };

                LauncherViewModel.LaunchModded(loaderMap, exArgs, serverRegister);

            } catch (Exception ex) {
                MessageBox.Show(ex.ToString());
            }

        }

        private async void LaunchServerHeadless() {
            Process? serverRegister = await MakeRegistrationProcess();
            if (serverRegister == null) {
                return;
            }

            try {
                //modify command line args and enable required mods for RCON connectivity
                string RCONMap = "agmods?map=frontend?rcon" + LauncherViewModel.BuildModsString() + "?listen"; //ensure the RCON zombie blueprint gets started

                string[] exArgs = {
                    $"-port {GamePort}", //specify server port
                    "-nullrhi", //disable rendering
                    $"-rcon {RconPort}", //let the serverplugin know that we want RCON running on the given port
                    "-RenderOffScreen", //super-disable rendering
                    "-unattended", //let it know no one's around to help
                    "-nosound" //disable sound
                };

                LauncherViewModel.LaunchModded(RCONMap, exArgs, serverRegister);
            } catch (Exception ex) {
                MessageBox.Show(ex.ToString());
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
                $"-n ^\"{ServerName.Replace("\"", "^\"")}^\" " +
                $"-d ^\"{ServerDescription.Replace("\"", "^\"").Replace("\n", "^\n")}^\" " +
                $"-r ^\"{ServerList}^\" " +
                $"-c ^\"{RconPort}^\"";
            serverRegister.StartInfo.Arguments = $"/c \"{registerCommand}\"";
            serverRegister.StartInfo.CreateNoWindow = false;

            return serverRegister;
        }
    }
}
