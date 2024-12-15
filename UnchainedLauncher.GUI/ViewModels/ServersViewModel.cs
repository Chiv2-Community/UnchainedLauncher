using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Input;
using UnchainedLauncher.Core.API;
using UnchainedLauncher.Core.API.A2S;
using UnchainedLauncher.Core.API.ServerBrowser;

namespace UnchainedLauncher.GUI.ViewModels {
    public partial class ServersViewModel : IDisposable, INotifyPropertyChanged {
        private bool disposedValue;

        public ObservableCollection<ServerViewModel> Servers { get; set; }
        public SettingsViewModel SettingsViewModel { get; set; }
        public int? Index { get; set; }
        public ICommand ShutdownCurrentTabCommand { get; private set; }

        private IServerBrowser CurrentBackend { get; set; }
        private Func<String, IServerBrowser> ServerBrowserBackendInitializer { get; }
        public IServerBrowser Backend {
            get {
                if (CurrentBackend.Host != SettingsViewModel.ServerBrowserBackend) {
                    var oldBackend = this.CurrentBackend;
                    var newBackend = ServerBrowserBackendInitializer(SettingsViewModel.ServerBrowserBackend);
                    CurrentBackend = newBackend;
                    // TODO: this is not valid because it could dispose a backend before
                    // the servers using it are disposed. This will cause those servers to
                    // use a disposed backend. (erroneous behavior) Disposal of the backend
                    // should be the responsability of the servers which hold references to them.
                    oldBackend.Dispose();
                }

                return CurrentBackend;
            }
        }

        public ServersViewModel(SettingsViewModel settings, Func<string, IServerBrowser>? createServerBrowserBackend) {
            ShutdownCurrentTabCommand = new RelayCommand(ShutdownCurrentTab);
            Servers = new ObservableCollection<ServerViewModel>();
            SettingsViewModel = settings;

            ServerBrowserBackendInitializer = createServerBrowserBackend ?? DefaultServerBrowserInitializer;
            CurrentBackend = ServerBrowserBackendInitializer(SettingsViewModel.ServerBrowserBackend);

            Index = null;

            // Automatically select the first server created
            Servers.CollectionChanged += (_, _) => {
                if (Index == null && Servers.Count > 0) {
                    Index = 0;
                }
                else if (Index != null && Servers.Count == 0) {
                    Index = null;
                }
            };
        }

        public void ShutdownAllServers() {
            foreach (ServerViewModel s in Servers) {
                s.Dispose();
            }
            Servers.Clear();
        }

        public void ShutdownCurrentTab() {
            if (Servers.Count == 0 || Index == null) { return; }
            var toKill = Servers[Index.Value];

            // pulling out the reference like this avoids a
            // potential TOCTOU error between the next two lines
            toKill.Dispose();
            Servers.Remove(toKill);
        }

        public ServerViewModel RegisterServer(string serverIp, int rconPort, C2ServerInfo serverInfo, Process serverProcess) {
            var a2s = DefaultA2SConnectionInitializer(serverIp, serverInfo.Ports.A2s);
            A2SBoundRegistration BoundRegistration = new(Backend, a2s, serverInfo, serverIp);
            var server = new Chivalry2Server(BoundRegistration);
            var serverVm = new ServerViewModel(server, serverProcess, rconPort);
            Servers.Add(serverVm);
            return serverVm;
        }

        protected virtual void Dispose(bool disposing) {
            if (!disposedValue) {
                if (disposing) {
                    ShutdownAllServers();
                    // backend is initialized by this class, so it should be disposed here.
                    this.CurrentBackend.Dispose();
                }

                disposedValue = true;
            }
        }

        public void Dispose() {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        private static Func<string, IServerBrowser> DefaultServerBrowserInitializer => (host) => new ServerBrowser(new Uri(host + "/api/v1"), new HttpClient());
        private static Func<string, int, A2S> DefaultA2SConnectionInitializer => (host, port) => new A2S(new IPEndPoint(IPAddress.Parse(host), port));
    }

    // TODO? listplayers integration.
    // 1. send listplayers to rcon
    // 2. get response from system clipboard
    // 3. neatly display response information in-window

    public partial class ServerViewModel : IDisposable, INotifyPropertyChanged {
        // TODO: make Chivalry2Server handle the game process, Pid, and Rcon stuff
        // instead of having the ViewModel do it
        public Chivalry2Server Server { get; private set; }
        public int Pid { get; private set; }
        public string CurrentRconCommand { get; set; } = "";
        public int RconPort { get; private set; }
        public ICommand SubmitRconCommand { get; private set; }
        public string RconHistory { get; set; }
        private static readonly IPAddress LocalHost = IPAddress.Parse("127.0.0.1");
        private readonly IPEndPoint RconEndPoint;
        private readonly Process? ServerProcess;

        private bool disposed = false;


        public ServerViewModel(Chivalry2Server server, Process serverProcess, int rconPort) {
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

        ~ServerViewModel() {
            Dispose(disposing: false);
        }
    }
}