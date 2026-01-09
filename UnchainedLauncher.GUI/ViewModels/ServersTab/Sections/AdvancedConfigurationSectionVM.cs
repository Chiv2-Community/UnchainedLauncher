using System.ComponentModel;
using UnchainedLauncher.GUI.ViewModels.ServersTab.IniSections;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab.Sections {
    public class AdvancedConfigurationSectionVM: INotifyPropertyChanged {
        private readonly ServerConfigurationVM _parent;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void RaisePropertyChanged(string propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void RaiseDiscordWarningPropertiesChanged() {
            RaisePropertyChanged(nameof(IsDiscordBotTokenMissingWarning));
            RaisePropertyChanged(nameof(IsDiscordChannelIdMissingWarning));
            RaisePropertyChanged(nameof(IsDiscordIntegrationIncompleteWarning));
        }

        public AdvancedConfigurationSectionVM(ServerConfigurationVM parent) {
            _parent = parent;

            if (_parent is INotifyPropertyChanged npc) {
                npc.PropertyChanged += (_, e) => {
                    if (e.PropertyName is nameof(ServerConfigurationVM.PlayerBotCount)
                        or nameof(ServerConfigurationVM.WarmupTime)
                        or nameof(ServerConfigurationVM.ShowInServerBrowser)
                        or nameof(ServerConfigurationVM.DiscordBotToken)
                        or nameof(ServerConfigurationVM.DiscordChannelId)) {
                        PropertyChanged?.Invoke(this, e);
                    }

                    if (e.PropertyName is nameof(ServerConfigurationVM.DiscordBotToken)
                        or nameof(ServerConfigurationVM.DiscordChannelId)) {
                        RaiseDiscordWarningPropertiesChanged();
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

        public string DiscordBotToken {
            get => _parent.DiscordBotToken;
            set {
                if (_parent.DiscordBotToken == value) {
                    return;
                }

                _parent.DiscordBotToken = value;
                RaisePropertyChanged(nameof(DiscordBotToken));
                RaiseDiscordWarningPropertiesChanged();
            }
        }

        public string DiscordChannelId {
            get => _parent.DiscordChannelId;
            set {
                if (_parent.DiscordChannelId == value) {
                    return;
                }

                _parent.DiscordChannelId = value;
                RaisePropertyChanged(nameof(DiscordChannelId));
                RaiseDiscordWarningPropertiesChanged();
            }
        }

        public bool IsDiscordBotTokenMissingWarning =>
            string.IsNullOrWhiteSpace(DiscordBotToken)
            && !string.IsNullOrWhiteSpace(DiscordChannelId);

        public bool IsDiscordChannelIdMissingWarning =>
            !string.IsNullOrWhiteSpace(DiscordBotToken)
            && string.IsNullOrWhiteSpace(DiscordChannelId);

        public bool IsDiscordIntegrationIncompleteWarning => IsDiscordBotTokenMissingWarning || IsDiscordChannelIdMissingWarning;
    }
}