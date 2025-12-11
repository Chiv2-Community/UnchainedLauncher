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
using UnchainedLauncher.Core.API.ServerBrowser;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab {

    public class ServerConfigurationCodec : DerivedJsonCodec<ObservableCollection<ServerConfiguration>, ObservableCollection<ServerConfigurationVM>> {

        public ServerConfigurationCodec(IModManager modManager) : base(
            ToJsonType,
            conf => ToClassType(conf, modManager)
        ) { }

        public static ObservableCollection<ServerConfigurationVM> ToClassType(ObservableCollection<ServerConfiguration> configurations, IModManager modManager) =>
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

        public PublicPorts ToPublicPorts() {
            return new PublicPorts(GamePort, PingPort, A2SPort);
        }

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

        public void EnableServerMod(Release release) => EnabledServerModList.Add(ReleaseCoordinates.FromRelease(release));
        public void DisableServerMod(Release release) => EnabledServerModList.Remove(ReleaseCoordinates.FromRelease(release));

        public void AddAvailableMod(Release release, string? previousVersion) {
            var existingMod = AvailableMods.Find(x => x.Manifest.RepoUrl == release.Manifest.RepoUrl);
            var existingMaps = existingMod.Map(x => x.Manifest.Maps).FirstOrDefault() ?? new List<string>();


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
            using (var defaultMapsListStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("UnchainedLauncher.GUI.Resources.DefaultMaps.txt")) {
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

        public ServerConfiguration ToServerConfiguration() => new ServerConfiguration(Name, Description, Password, LocalIp, GamePort, RconPort, A2SPort, PingPort, SelectedMap, ShowInServerBrowser, EnabledServerModList);

        public override string ToString() {
            var enabledMods = EnabledServerModList != null
                ? string.Join(", ", EnabledServerModList.Select(mod => mod?.ToString() ?? "null"))
                : "null";
            return $"ServerConfigurationVM({Name}, {Description}, {Password}, {LocalIp}, {GamePort}, {RconPort}, {A2SPort}, {PingPort}, {SelectedMap}, {ShowInServerBrowser}, [{enabledMods}])";
        }
    }
}