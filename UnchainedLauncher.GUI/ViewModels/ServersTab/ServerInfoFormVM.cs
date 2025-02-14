﻿using CommunityToolkit.Mvvm.Input;
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
using UnchainedLauncher.Core.Services.Processes.Chivalry;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab {

    [AddINotifyPropertyChangedInterface]
    public record class ServerInfoFormData(string LocalIp,
                                            string Name = "My Server",
                                            string Description = "My Server Description",
                                            string Password = "",
                                            int GamePort = 7777,
                                            int RconPort = 9001,
                                            int A2SPort = 7071,
                                            int PingPort = 3075,
                                            string SelectedMap = "FFA_Courtyard",
                                            bool ShowInServerBrowser = false) {
        public ServerLaunchOptions ToServerLaunchOptions(bool headless) {
            return new ServerLaunchOptions(
                headless,
                Name,
                Description,
                Option<string>.Some(Password).Map(pw => pw.Trim()).Filter(pw => pw != ""),
                SelectedMap,
                GamePort,
                PingPort,
                A2SPort,
                RconPort
            );
        }

        public PublicPorts ToPublicPorts() {
            return new PublicPorts(GamePort, PingPort, A2SPort);
        }
    }

    [AddINotifyPropertyChangedInterface]
    public partial class ServerInfoFormVM : INotifyPropertyChanged {
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
        public ObservableCollection<string> MapsList { get; set; }

        public ServerInfoFormVM(ObservableCollection<string>? mapsList = null,
                                ServerInfoFormData? data = null) {
            var initialData = data ?? new(GetAllLocalIPv4().FirstOrDefault("127.0.0.1"));
            MapsList = mapsList ?? new(GetDefaultMaps());
            SelectedMap = MapsList.FirstOrDefault("FFA_Courtyard");
            Name = initialData.Name;
            Description = initialData.Description;
            Password = initialData.Password;
            RconPort = initialData.RconPort;
            A2SPort = initialData.A2SPort;
            PingPort = initialData.PingPort;
            GamePort = initialData.GamePort;
            ShowInServerBrowser = initialData.ShowInServerBrowser;
            LocalIp = initialData.LocalIp;
        }

        [RelayCommand]
        public void AutoFillIp() {
            LocalIp = GetAllLocalIPv4().FirstOrDefault("127.0.0.1");
        }

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

        public ServerInfoFormData Data => new ServerInfoFormData(
            LocalIp, Name, Description, Password, GamePort, RconPort,
            A2SPort, PingPort, SelectedMap, ShowInServerBrowser
        );

        // https://stackoverflow.com/a/24814027
        public static string[] GetAllLocalIPv4() {
            List<string> ipAddrList = new List<string>();
            foreach (var item in NetworkInterface.GetAllNetworkInterfaces()) {
                if (item.OperationalStatus == OperationalStatus.Up) {
                    foreach (var ip in item.GetIPProperties().UnicastAddresses) {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork) {
                            ipAddrList.Add(ip.Address.ToString());
                        }
                    }
                }
            }
            return ipAddrList.ToArray();
        }
    }
}