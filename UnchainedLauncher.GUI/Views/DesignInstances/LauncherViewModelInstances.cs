using LanguageExt;
using System.Collections.Generic;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.GUI.ViewModels;

namespace UnchainedLauncher.GUI.Views.DesignInstances {
    public static class LauncherViewModelInstances {
        public static LauncherViewModel DEFAULT => new LauncherViewModel(
            SettingsViewModelInstances.DEFAULT,
            new ModManager(new HashMap<IModRegistry, IEnumerable<Mod>>(), new List<Release>()),
            null, null, null,
            null, new FileInfoVersionExtractor(), new MessageBoxSpawner()
        );
    }
}