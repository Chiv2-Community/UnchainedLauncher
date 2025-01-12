using System;
using System.Diagnostics;
using System.Net;
using UnchainedLauncher.Core.API;
using UnchainedLauncher.Core.API.A2S;
using UnchainedLauncher.Core.API.ServerBrowser;
using UnchainedLauncher.GUI.ViewModels.ServersTab;

namespace UnchainedLauncher.GUI.Views.DesignInstances {
    public static class ServerViewModelInstances {
        public static ServerVM DEFAULT => CreateDefaultServerViewModel();

        private static ServerVM CreateDefaultServerViewModel() {
            var rconHistoryString = "Hello, this is a fake server\n";
            rconHistoryString += "Ignore my output\n";
            rconHistoryString += "I'm just here to make the GUI look nice\n";
            rconHistoryString += "While you design it\n";

            return new ServerVM(
                new Chivalry2Server(
                    new A2SBoundRegistration(
                        new NullServerBrowser(),
                        new A2S(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 1111)),
                        new C2ServerInfo {
                            Description = "Design View Only. Do not use default constructor.",
                            Name = "Test Server",
                            Mods = Array.Empty<ServerBrowserMod>(),
                            PasswordProtected = false,
                            Ports = new PublicPorts(123, 456, 789)
                        },
                        "127.0.0.1"
                    )
                ),
                new Process(),
                1520
            ) {
                RconHistory = rconHistoryString
            };
        }
    }
}