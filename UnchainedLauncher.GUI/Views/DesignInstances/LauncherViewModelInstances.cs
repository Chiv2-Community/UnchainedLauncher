using LanguageExt;
using System.Collections.Generic;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Mods;
using UnchainedLauncher.Core.Mods.Registry;
using UnchainedLauncher.GUI.ViewModels;

namespace UnchainedLauncher.GUI.Views.DesignInstances {
    public static class LauncherViewModelInstances {
        public static LauncherViewModel DEFAULT => new LauncherViewModel(
            SettingsViewModelInstances.DEFAULT,
            new ModManager(new HashMap<IModRegistry, IEnumerable<Mod>>(), new List<Release>()),
            null, null, null,
            null
        );
    }
}