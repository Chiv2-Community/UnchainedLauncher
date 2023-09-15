using C2GUILauncher.JsonModels;
using PropertyChanged;

namespace C2GUILauncher.ViewModels {
    [AddINotifyPropertyChangedInterface]
    public class ServerSettingsViewModel {
        private static readonly string SettingsFilePath = $"{FilePaths.ModCachePath}\\unchained_launcher_server_settings.json";

        public string ServerName { get; set; }
        public string ServerDescription { get; set; }
        public string ServerList { get; set; }
        public int GamePort { get; set; }
        public int RconPort { get; set; }
        public int A2sPort { get; set; }
        public int PingPort { get; set; }

        public FileBackedSettings<ServerSettings> SettingsFile { get; set; }

        //may want to add a mods list here as well,
        //in the hopes of having multiple independent servers running one one machine
        //whose settings can be stored/loaded from files

        public ServerSettingsViewModel(string serverName, string serverDescription, string serverList, int gamePort, int rconPort, int a2sPort, int pingPort, FileBackedSettings<ServerSettings> settingsFile) {
            ServerName = serverName;
            ServerDescription = serverDescription;
            ServerList = serverList;
            GamePort = gamePort;
            RconPort = rconPort;
            A2sPort = a2sPort;
            PingPort = pingPort;

            SettingsFile = settingsFile;
        }

        public static ServerSettingsViewModel LoadSettings() {
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

            return new ServerSettingsViewModel(
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
                new ServerSettings(ServerName, ServerDescription, ServerList, GamePort, RconPort, A2sPort, PingPort)
            );
        }

    }
}
