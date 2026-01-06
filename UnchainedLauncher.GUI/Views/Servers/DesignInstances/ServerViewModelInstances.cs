using System;
using System.Diagnostics;
using System.Net;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.GUI.ViewModels.ServersTab;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Text.Json;
using Unchained.ServerBrowser.Api;
using UnchainedLauncher.Core.Services.Server;

namespace UnchainedLauncher.GUI.Views.Servers.DesignInstances {
    public static class ServerViewModelInstances {
        public static ServerVM DEFAULT => new ServerDesignVM();
    }

    public class ServerDesignVM : ServerVM {
        public ServerDesignVM() : base(
            new Chivalry2Server(
                new Process(),
                CreateDesignRegistrationService(),
                new RCON(new IPEndPoint(IPAddress.Loopback, 1520))
            )
        ) {
            RconHistory = "Hello, this is a fake server\n" +
                          "Ignore my output\n" +
                          "I'm just here to make the GUI look nice\n" +
                          "While you design it\n";
        }

        private static ServerRegistrationService CreateDesignRegistrationService() {
            var loggerFactory = LoggerFactory.Create(builder => { });
            var logger = loggerFactory.CreateLogger<DefaultApi>();
            var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
            var jsonProvider = new Unchained.ServerBrowser.Client.JsonSerializerOptionsProvider(new JsonSerializerOptions());
            var events = new DefaultApiEvents();
            IDefaultApi api = new DefaultApi(logger, loggerFactory, httpClient, jsonProvider, events);
            // Do not start the service in design mode to avoid network calls
            return new ServerRegistrationService(api);
        }
    }
}