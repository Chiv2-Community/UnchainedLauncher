using PropertyChanged;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Input;
using System.Collections.Generic;
using System.Reflection;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using log4net;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.GUI.JsonModels;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.Core.Mods;
using UnchainedLauncher.Core;
using LanguageExt;

namespace UnchainedLauncher.GUI.ViewModels {
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

        public ServerLauncherViewModel(LauncherViewModel launcherViewModel, SettingsViewModel settingsViewModel, ModManager modManager, string serverName, string serverDescription, string serverPassword, string selectedMap, short gamePort, short rconPort, short a2sPort, short pingPort, bool showInServerBrowser, FileBackedSettings<ServerSettings> settingsFile) {
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
            ModManager = modManager;

            LaunchServerCommand = new RelayCommand(LaunchServer);
            LaunchServerHeadlessCommand = new RelayCommand(LaunchServerHeadless);

            MapsList = new ObservableCollection<string>();

            using (Stream? defaultMapsListStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("UnchainedLauncherGUI.Resources.DefaultMaps.txt")) {
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

        public static ServerLauncherViewModel LoadSettings(LauncherViewModel launcherViewModel, SettingsViewModel settingsViewModel, ModManager modManager) {
            var fileBackedSettings = new FileBackedSettings<ServerSettings>(SettingsFilePath);
            var loadedSettings = fileBackedSettings.LoadSettings();

            return new ServerLauncherViewModel(
                launcherViewModel,
                settingsViewModel,
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

        private async void LaunchServer() {
            await RunServerLaunch(false);

        }

        private async void LaunchServerHeadless() {
            await RunServerLaunch(true);
        }

        private async Task RunServerLaunch(bool headless) {
            CanClick = false;
            try {
                var serverMods = ModManager.EnabledModReleases
                    .Select(mod => mod.Manifest)
                    .Where(manifest => manifest.ModType == ModType.Server || manifest.ModType == ModType.Shared);

                Process? serverRegister = await MakeRegistrationProcess();
                if (serverRegister == null) {
                    return;
                }

                var serverLaunchOptions = new ServerLaunchOptions(
                    headless,
                    Prelude.Some(ServerPassword).Map(pw => pw.Trim()).Filter(pw => pw != ""),
                    SelectedMap,
                    GamePort,
                    PingPort,
                    A2sPort,
                    RconPort
                );

                LauncherViewModel.LaunchModded(Prelude.Some(serverLaunchOptions));
                CanClick = LauncherViewModel.CanClick;

            } catch (Exception ex) {
                MessageBox.Show("Failed to launch. Check the logs for details.");
                logger.Error("Failed to launch.", ex);
                CanClick = LauncherViewModel.CanClick;
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
                registerCommand += "--no-register ";
            }

            if (ServerPassword.Trim() != "") {
                registerCommand += $"-x ";
            }

            logger.Info($"Running registration: {registerCommand}");

            serverRegister.StartInfo.Arguments = $"/C \"{registerCommand}\"";
            serverRegister.StartInfo.CreateNoWindow = false;

            return serverRegister;
        }
    }
}
