using CommunityToolkit.Mvvm.Input;
using PropertyChanged;
using System;
using System.Threading.Tasks;
using System.Windows.Media;
using UnchainedLauncher.Core.Services.Server;
using UnchainedLauncher.Core.Services.Server.A2S;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab {
    // TODO? listplayers integration.
    // 1. send listplayers to rcon
    // 2. get response from system clipboard
    // 3. neatly display response information in-window

    [AddINotifyPropertyChangedInterface]
    public partial class ServerVM : IDisposable, System.ComponentModel.INotifyPropertyChanged {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

        // TODO: make Chivalry2Server handle the game process, Pid, and Rcon stuff
        // instead of having the ViewModel do it
        public Chivalry2Server Server { get; private set; }
        public string CurrentRconCommand { get; set; } = "";

        public string? LastA2SExceptionMessage => Server.RegistrationHandler?.RegistrationState
        
        // Helper for View compatibility
        public ServerRegistrationOptions? ServerInfo => Server.RegistrationHandler?.Options;

        // Color properties for A2S status
        public Brush A2SStatusColor => A2SStatus switch {
            this.A2SStatus.AwaitingServerStart => new SolidColorBrush(Colors.Orange), // Yellow
            this.A2SStatus.Active => new SolidColorBrush(Colors.Green),
            this.A2SStatus.Reconnecting => new SolidColorBrush(Colors.Orange), // Yellow (will flash in XAML)
            this.A2SStatus.Dead => new SolidColorBrush(Colors.Red),
            _ => new SolidColorBrush(Colors.Gray)
        };
        
        public bool IsA2SReconnecting => A2SStatus == this.A2SStatus.Reconnecting;
        
        public string RconHistory { get; set; }

        private bool _disposed = false;


        public ServerVM(Chivalry2Server server) {
            // TODO: move serverProcess and rconPort out of here and into the Chivalry2Server class.
            // That class should also handle the following:
            // 1. killing the process
            // 2. doing restarts on crash (?)
            // 3. sending RCON commands
            this.Server = server;
            this.RconHistory = "";
            
            if (this.Server?.RegistrationHandler != null)
            {
                this.Server.RegistrationHandler.PropertyChanged += RegistrationHandler_PropertyChanged;
                if (this.Server.RegistrationHandler.A2SWatcher != null)
                {
                    this.Server.RegistrationHandler.A2SWatcher.PropertyChanged += A2SWatcher_PropertyChanged;
                }
            }
        }

        private void RegistrationHandler_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(ServerRegistrationService.A2SWatcher)) {
                if (Server.RegistrationHandler.A2SWatcher != null) {
                    Server.RegistrationHandler.A2SWatcher.PropertyChanged += A2SWatcher_PropertyChanged;
                }
                NotifyA2SProperties();
            }
            if (e.PropertyName == nameof(ServerRegistrationService.Options)) {
                OnPropertyChanged(nameof(ServerInfo));
            }
        }

        private void A2SWatcher_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
            NotifyA2SProperties();
        }

        private void NotifyA2SProperties() {
            OnPropertyChanged(nameof(A2SStatus));
            OnPropertyChanged(nameof(A2SStatusColor));
            OnPropertyChanged(nameof(IsA2SOk));
            OnPropertyChanged(nameof(LastA2SExceptionMessage));
            OnPropertyChanged(nameof(IsA2SReconnecting));
            OnPropertyChanged(nameof(IsA2SStarting));
        }

        [RelayCommand]
        public async Task SubmitRcon() {
            var command = CurrentRconCommand;
            CurrentRconCommand = "";

            if (command == "") return;
            try {
                if (Server.Rcon == null) {
                    RconHistory += $"ERR: No Rcon endpoint.";
                    return;
                }
                await Server.Rcon.SendCommand(command);
                RconHistory += $"{command}\n";
            }
            catch (Exception e) {
                RconHistory += $"ERR: {e.Message}\n";
            }
        }

        // IDisposable stuff ensures timely DELETE request to backend
        // and closing of the chiv process associated with the server
        protected virtual void Dispose(bool disposing) {
            if (!_disposed && disposing) {
                Server.Dispose();
            }

            _disposed = true;
        }

        public void Dispose() {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        ~ServerVM() {
            Dispose(disposing: false);
        }
    }
}