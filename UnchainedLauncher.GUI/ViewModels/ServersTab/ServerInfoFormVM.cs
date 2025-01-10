using PropertyChanged;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab {
    [AddINotifyPropertyChangedInterface]
    public class ServerInfoFormVM {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Password { get; set; }
        public int GamePort { get; set; }
        public int RconPort { get; set; }
        public int A2sPort { get; set; }
        public int PingPort { get; set; }
        public string SelectedMap { get; set; }
        public bool ShowInServerBrowser { get; set; }
        public ObservableCollection<string> MapsList { get; set; }
        public ServerInfoFormVM(IEnumerable<string> mapsList,
                                string name = "My Server",
                                string description = "My Server Description",
                                string password = "",
                                int gamePort = 7777,
                                int rconPort = 9001,
                                int a2sPort = 7071,
                                int pingPort = 3075,
                                bool showInServerBrowser = false) {
            MapsList = new ObservableCollection<string>(mapsList);
            SelectedMap = MapsList.FirstOrDefault("FFA_Courtyard");

            Name = name;
            Description = description;
            Password = password;
            RconPort = rconPort;
            A2sPort = a2sPort;
            PingPort = pingPort;
            GamePort = gamePort;
            ShowInServerBrowser = showInServerBrowser;
        }
    }
}
