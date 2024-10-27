using CommunityToolkit.Mvvm.Input;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UnchainedLauncher.Core.API;

namespace UnchainedLauncher.GUI.ViewModels
{
    [AddINotifyPropertyChangedInterface]
    public class ServersViewModel : IDisposable
    {
        public ObservableCollection<ServerViewModel> Servers { get; set; }
        public SettingsViewModel SettingsViewModel { get; set; }
        public int Index { get; set; }
        public ICommand ShutdownCurrentTabCommand { get; private set; }

        private IServerBrowser CurrentBackend { get; set; }
        private Func<String, IServerBrowser> ServerBrowserBackendInitializer { get; }
        public IServerBrowser Backend { get {
            if(CurrentBackend.Host != SettingsViewModel.ServerBrowserBackend) {
                CurrentBackend.Dispose();
                    
                var newBackend = ServerBrowserBackendInitializer(SettingsViewModel.ServerBrowserBackend);
                CurrentBackend = newBackend;
            }

            return CurrentBackend;
        }}

        public ServersViewModel(SettingsViewModel settings, Func<string, IServerBrowser>? createServerBrowserBackend)
        {
            ShutdownCurrentTabCommand = new RelayCommand(ShutdownCurrentTab);
            Servers = new ObservableCollection<ServerViewModel>();
            SettingsViewModel = settings;

            ServerBrowserBackendInitializer = createServerBrowserBackend ?? DefaultServerBrowserInitializer;
            CurrentBackend = ServerBrowserBackendInitializer(SettingsViewModel.ServerBrowserBackend);
        }

        public void ShutdownAllServers()
        {
            foreach (ServerViewModel s in Servers) {
                s.Dispose();
            }
            Servers.Clear();
        }

        public void ShutdownCurrentTab()
        {
            if(Servers.Length() == 0) { return; }
            var toKill = Servers[Index];
            // pulling out the reference like this avoids a
            // potential TOCTOU error between the next two lines
            toKill.Dispose();
            Servers.Remove(toKill);
        }

        public ServerViewModel RegisterServer(string serverIp, int rconPort, C2ServerInfo serverInfo, Process serverProcess) {
            var a2s = DefaultA2SConnectionInitializer(serverIp, serverInfo.Ports.A2s);
            var server = new RegisteredServer(Backend, a2s, serverInfo, serverIp);
            var serverVm = new ServerViewModel(server, serverProcess, rconPort);
            Servers.Add(serverVm);
            return serverVm;
        }

        public void Dispose() {
            ShutdownAllServers();

            // backend is initialized by this class, so it should be disposed here.
            CurrentBackend.Dispose();
        }

        private static Func<string, IServerBrowser> DefaultServerBrowserInitializer => (host) => new ServerBrowser(new Uri(host + "/api/v1"), new HttpClient());
        private static Func<string, int, A2S> DefaultA2SConnectionInitializer => (host, port) => new A2S(new IPEndPoint(IPAddress.Parse(host), port));
    }

    // TODO? listplayers integration.
    // 1. send listplayers to rcon
    // 2. get response from system clipboard
    // 3. neatly display response information in-window

    [AddINotifyPropertyChangedInterface]
    public class ServerViewModel : IDisposable
    {
        public RegisteredServer Server { get; private set; }
        public int Pid { get; private set; }
        public string CurrentRconCommand { get; set; } = "";
        public int RconPort { get; private set; }
        public ICommand SubmitRconCommand { get; private set; }
        public string RconHistory { get; set; }
        private static readonly IPAddress LocalHost = IPAddress.Parse("127.0.0.1");
        private readonly IPEndPoint RconEndPoint;
        private readonly Process? ServerProcess;

        private bool disposed = false;

        public ServerViewModel(RegisteredServer server, Process serverProcess, int rconPort)
        {
            this.Server = server;
            this.RconPort = rconPort;
            this.RconHistory = "";
            this.ServerProcess = serverProcess;
            this.RconEndPoint = new IPEndPoint(LocalHost, RconPort);
            SubmitRconCommand = new AsyncRelayCommand(SubmitCommand);
        }

        public async Task SubmitCommand()
        {
            var command = CurrentRconCommand;
            CurrentRconCommand = "";
            await SendCommand(command);
        }

        public async Task SendCommand(string command) {
            if (command == "") {
                return;
            }
            try {
                if (!Server.IsA2SOk) {
                    RconHistory += $"INF: Command will be sent when A2S is good\n";
                    await Server.WhenA2SOk();
                }
                await RCON.SendCommandTo(RconEndPoint, command);
                RconHistory += $"{command}\n";
            } catch (Exception e) {
                RconHistory += $"ERR: {e.Message}\n";
            }
        }

        // IDisposable stuff ensures timely DELETE request to backend
        // and closing of the chiv process associated with the server
        protected virtual void Dispose(bool disposing)
        {
            if (!disposed && disposing)
            {
                // TODO: might help to send an `exit` rcon command
                Server.Dispose();
                ServerProcess?.Kill();
                ServerProcess?.Close();
                ServerProcess?.Dispose();
            }

            disposed = true;
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        ~ServerViewModel()
        {
            Dispose(disposing: false);
        }
    }
}
