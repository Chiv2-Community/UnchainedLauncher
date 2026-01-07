using PropertyChanged;
using UnchainedLauncher.Core.INIModels.Engine;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab.IniSections {
    [AddINotifyPropertyChangedInterface]
    public class IpNetDriverSectionVM {
        public int NetServerMaxTickRate { get; set; }
        public int MaxClientRate { get; set; }
        public int MaxInternetClientRate { get; set; }
        public float InitialConnectTimeout { get; set; }
        public float ConnectionTimeout { get; set; }
        public int LanServerMaxTickRate { get; set; }
        public float RelevantTimeout { get; set; }
        public int SpawnPrioritySeconds { get; set; }
        public float ServerTravelPause { get; set; }

        public void LoadFrom(IpNetDriver model) {
            NetServerMaxTickRate = model.NetServerMaxTickRate;
            MaxClientRate = model.MaxClientRate;
            MaxInternetClientRate = model.MaxInternetClientRate;
            InitialConnectTimeout = model.InitialConnectTimeout;
            ConnectionTimeout = model.ConnectionTimeout;
            LanServerMaxTickRate = model.LanServerMaxTickRate;
            RelevantTimeout = model.RelevantTimeout;
            SpawnPrioritySeconds = model.SpawnPrioritySeconds;
            ServerTravelPause = model.ServerTravelPause;
        }

        public IpNetDriver ToModel() => new(
            NetServerMaxTickRate,
            MaxClientRate,
            MaxInternetClientRate,
            InitialConnectTimeout,
            ConnectionTimeout,
            LanServerMaxTickRate,
            RelevantTimeout,
            SpawnPrioritySeconds,
            ServerTravelPause
        );
    }
}
