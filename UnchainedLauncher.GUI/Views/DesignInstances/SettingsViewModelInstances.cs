using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.Services.Installer;
using UnchainedLauncher.GUI.JsonModels;
using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.GUI.ViewModels;
using UnchainedLauncher.GUI.Views.Registry.DesignInstances;

namespace UnchainedLauncher.GUI.Views.DesignInstances {
    public static class SettingsViewModelInstances {
        public static SettingsVM DEFAULT => new SettingsDesignVM();
    }

    public class SettingsDesignVM() : SettingsVM(RegistryWindowViewModelInstances.DEFAULT,
        new RegistryWindowService(),
        InstallationType.Steam,
        true,
        true,
        "--design-time-only-default-constructor",
        "https://servers.polehammer.net",
        false,
        false,
        "");
}