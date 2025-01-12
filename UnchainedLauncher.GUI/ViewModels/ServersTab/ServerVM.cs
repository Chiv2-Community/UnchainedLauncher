using CommunityToolkit.Mvvm.Input;
using PropertyChanged;
using System;
using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Input;
using UnchainedLauncher.Core.API;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab {
    // TODO? listplayers integration.
    // 1. send listplayers to rcon
    // 2. get response from system clipboard
    // 3. neatly display response information in-window

    [AddINotifyPropertyChangedInterface]
    public partial class ServerVM : IDisposable {
        // TODO: make Chivalry2Server handle the game process, Pid, and Rcon stuff
        // instead of having the ViewModel do it
        public Chivalry2Server Server { get; private set; }
        public string CurrentRconCommand { get; set; } = "";
        public int RconPort { get; private set; }
        public ICommand SubmitRconCommand { get; private set; }
        // TODO: This binding won't work and I've spent enough time tearing my hair out to fix it for now.
        // I think the property changed notification is not getting propigated up when LastException is changed.
        // Swapping to a different template and coming back will show the updated value in the tooltip
        public string? LastA2sExceptionMessage => Server.RegistrationHandler.A2SWatcher.LastException?.Message;
        public bool IsA2sOk => Server.RegistrationHandler.A2SWatcher.A2sOk;
        public string RconHistory { get; set; }
        private static readonly IPAddress LocalHost = IPAddress.Loopback;
        private readonly IPEndPoint RconEndPoint;
        public readonly Process? ServerProcess;

        private bool disposed = false;


        public ServerVM(Chivalry2Server server, Process serverProcess, int rconPort) {
            // TODO: move serverProcess and rconPort out of here and into the Chivalry2Server class.
            // That class should also handle the following:
            // 1. killing the process
            // 2. doing restarts on crash (?)
            // 3. sending RCON commands
            this.Server = server;
            this.RconPort = rconPort;
            this.RconHistory = "";
            this.ServerProcess = serverProcess;
            this.RconEndPoint = new IPEndPoint(LocalHost, RconPort);
            SubmitRconCommand = new AsyncRelayCommand(SubmitCommand);
        }

        public async Task SubmitCommand() {
            var command = CurrentRconCommand;
            CurrentRconCommand = "";
            await SendCommand(command);
        }

        public async Task SendCommand(string command) {
            if (command == "") {
                return;
            }
            try {
                await RCON.SendCommandTo(RconEndPoint, command);
                RconHistory += $"{command}\n";
            }
            catch (Exception e) {
                RconHistory += $"ERR: {e.Message}\n";
            }
        }

        // IDisposable stuff ensures timely DELETE request to backend
        // and closing of the chiv process associated with the server
        protected virtual void Dispose(bool disposing) {
            if (!disposed && disposing) {
                // TODO: might help to send an `exit` rcon command
                ServerProcess?.Kill();
                ServerProcess?.Close();
                ServerProcess?.Dispose();
                Server.Dispose();
            }

            disposed = true;
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