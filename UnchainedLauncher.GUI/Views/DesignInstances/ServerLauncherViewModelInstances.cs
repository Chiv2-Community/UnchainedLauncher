using LanguageExt;
using System.Collections.Generic;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Mods;
using UnchainedLauncher.Core.Mods.Registry;
using UnchainedLauncher.GUI.JsonModels;
using UnchainedLauncher.GUI.Utilities;
using UnchainedLauncher.GUI.ViewModels;

namespace UnchainedLauncher.GUI.Views.DesignInstances {
    class ServerLauncherViewModelInstances {
        public static ServerLauncherViewModel DEFAULT => new ServerLauncherViewModel(
            SettingsViewModelInstances.DEFAULT,
            ServersViewModelInstances.DEFAULT,
            null,
            new ModManager(new HashMap<IModRegistry, IEnumerable<Mod>>(), new List<Release>()),
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