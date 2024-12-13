using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnchainedLauncher.Core.Installer;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.GUI.JsonModels;
using UnchainedLauncher.GUI.ViewModels;

namespace UnchainedLauncher.GUI.Views.DesignInstances {
    public static class SettingsViewModelInstances {
        public static SettingsViewModel DEFAULT => new SettingsViewModel(
            new MockInstaller(),
            null,
            InstallationType.Steam,
            true,
            "--design-time-only-default-constructor",
            "https://servers.polehammer.net",
            new FileBackedSettings<LauncherSettings>(""), "", (_) => { }
        );
    }
}