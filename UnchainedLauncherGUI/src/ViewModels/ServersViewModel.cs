using CommunityToolkit.Mvvm.Input;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UnchainedLauncher.Core.API;

namespace UnchainedLauncher.GUI.ViewModels
{
    [AddINotifyPropertyChangedInterface]
    public class ServersViewModel
    {
        public ObservableCollection<ServerViewModel> Servers { get; set; }
        public int Index { get; set; }
        private Window Window;
        public ICommand ShutdownCurrentTabCommand { get; private set; }

        // TODO: link this up with the global understanding of what the backend is after test
        private static readonly Uri backend = new("http://localhost:8080/api/v1");

        public ServersViewModel(Window window)
        {
            ShutdownCurrentTabCommand = new RelayCommand(ShutdownCurrentTab);
            this.Window = window;
            window.Closed += ShutdownAllServers;
            //Servers = new List<RegisteredServer>(); //TODO: use this to replace test values
            //initialize with test servers
            Servers = new ObservableCollection<ServerViewModel>{ 
                new(window, new RegisteredServer(backend, new C2ServerInfo(){ Name="test1", Description="test1"}, "127.0.0.1")),
                new(window, new RegisteredServer(backend, new C2ServerInfo(){ Name="test2", Description="test2"}, "127.0.0.1")),
                new(window, new RegisteredServer(backend, new C2ServerInfo(){ Name="test3", Description="test3"}, "127.0.0.1")),
                new(window, new RegisteredServer(backend, new C2ServerInfo(){ Name="test4", Description="test4"}, "127.0.0.1")),
            };
        }

        public void ShutdownAllServers(object? sender, EventArgs e)
        {
            foreach(ServerViewModel s in Servers)
            {
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
    }

    // TODO? listplayers integration.
    // 1. send listplayers to rcon
    // 2. get response from system clipboard
    // 3. neatly display response information in-window

    // TODO: add output node for errors relating to server
    // registration. Similar to what A2S has right now
    [AddINotifyPropertyChangedInterface]
    public class ServerViewModel : IDisposable
    {
        public RegisteredServer Server { get; private set; }
        public int pid { get; private set; }
        public string CurrentRconCommand { get; set; } = "";
        public int RconPort { get; private set; }
        public ICommand SubmitRconCommand { get; private set; }
        public string RconHistory { get; set; }
        private static readonly IPAddress LocalHost = IPAddress.Parse("127.0.0.1");
        private Process? ServerProcess;

        private bool disposed = false;

        public ServerViewModel(Window window, RegisteredServer server, Process? serverProcess = null, int rconPort = 9001)
        {
            this.Server = server;
            this.RconPort = rconPort;
            this.RconHistory = "";
            this.ServerProcess = serverProcess;
            SubmitRconCommand = new RelayCommand(SubmitCommand);
        }

        public async void SubmitCommand()
        {
            if (CurrentRconCommand == "")
            {
                return;
            }

            try
            {
                string commandToSend = this.CurrentRconCommand;
                CurrentRconCommand = "";
                if (!Server.IsA2SOk)
                {
                    RconHistory += $"INF: Command will be sent when A2S is good\n";
                    await Server.WhenA2SOk();
                }
                await RCON.SendCommandTo(new IPEndPoint(LocalHost, RconPort), commandToSend);
                RconHistory += $"{commandToSend}\n";
            }
            catch(Exception e)
            {
                RconHistory += $"ERR: {e.Message}\n";
            }
        }

        // IDisposable stuff ensures timely DELETE request to backend
        // and closing of the chiv process associated with the server
        protected virtual void Dispose(bool disposing)
        {
            if (!disposed && disposing)
            {
                // TODO: make this close the process too
                // might help to send an `exit` rcon command
                Server.Dispose();
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
