using System.ComponentModel;
using UnchainedLauncher.GUI.ViewModels.ServersTab.IniSections;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab.Sections {
    public class AdvancedConfigurationSectionVM : INotifyPropertyChanged {
        private readonly ServerConfigurationVM _parent;

        public event PropertyChangedEventHandler? PropertyChanged;

        public AdvancedConfigurationSectionVM(ServerConfigurationVM parent) {
            _parent = parent;

            if (_parent is INotifyPropertyChanged npc) {
                npc.PropertyChanged += (_, e) => {
                    if (e.PropertyName is nameof(ServerConfigurationVM.PlayerBotCount)
                        or nameof(ServerConfigurationVM.WarmupTime)
                        or nameof(ServerConfigurationVM.ShowInServerBrowser)) {
                        PropertyChanged?.Invoke(this, e);
                    }
                };
            }
        }

        public IpNetDriverSectionVM IpNetDriver => _parent.IpNetDriver;
        public TBLGameModeSectionVM GameMode => _parent.GameMode;

        public int? PlayerBotCount {
            get => _parent.PlayerBotCount;
            set => _parent.PlayerBotCount = value;
        }

        public int? WarmupTime {
            get => _parent.WarmupTime;
            set => _parent.WarmupTime = value;
        }

        public bool ShowInServerBrowser {
            get => _parent.ShowInServerBrowser;
            set => _parent.ShowInServerBrowser = value;
        }
    }
}