using LanguageExt.Pipes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnchainedLauncher.Core.API;
using UnchainedLauncher.Core.API.A2S;
using UnchainedLauncher.Core.API.ServerBrowser;
using UnchainedLauncher.GUI.ViewModels;

namespace UnchainedLauncher.GUI.Views.DesignInstances {
    public static class ServersViewModelInstances {
        public static ServersViewModel DEFAULT => CreateDefaultServersViewModel();

        private static ServersViewModel CreateDefaultServersViewModel() {
            var instance = new ServersViewModel(SettingsViewModelInstances.DEFAULT, null);
            instance.Servers.Add(ServerViewModelInstances.DEFAULT);
            instance.Servers.Add(ServerViewModelInstances.DEFAULT);
            return instance;
        }
    }
}