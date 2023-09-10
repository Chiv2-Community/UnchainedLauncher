using PropertyChanged;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;

namespace C2GUILauncher.ViewModels {
    [AddINotifyPropertyChangedInterface]
    public class ServerSettingsViewModel {
        public string serverName { get; set; } = "Chivalry 2 server";
        public string serverDescription { get; set; } = "Example description";
        public string serverList { get; set; } = "https://servers.polehammer.net";
        public int gamePort { get; set; } = 7777;
        public int rconPort { get; set; } = 9001;
        public int a2sPort { get; set; } = 7071;
        public int pingPort { get; set; } = 3075;

        //may want to add a mods list here as well,
        //in the hopes of having multiple independent servers running one one machine
        //whose settings can be stored/loaded from files
    }
}
