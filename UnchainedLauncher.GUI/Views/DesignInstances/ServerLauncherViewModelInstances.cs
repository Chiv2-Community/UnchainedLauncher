using LanguageExt;
using System.Collections.Generic;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.GUI.JsonModels;
using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.GUI.ViewModels;

namespace UnchainedLauncher.GUI.Views.DesignInstances {
    class ServerLauncherViewModelInstances {
        public static ServerLauncherVM DEFAULT => new ServerLauncherVM(
            SettingsViewModelInstances.DEFAULT,
            ServersViewModelInstances.DEFAULT,
            null,
            new ModManager(new HashMap<IModRegistry, IEnumerable<Core.JsonModels.Metadata.V3.Mod>>(), new List<Release>()),
            new MessageBoxSpawner(),
            "Chivalry 2 server",
            "Design time test description",
            "",
            "FFA_Courtyard",
            7777,
            9001,
            7071,
            3075,
            true,
            new FileBackedSettings<ServerSettings>("")
        );
    }
}