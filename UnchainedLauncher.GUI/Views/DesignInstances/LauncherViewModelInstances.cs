using System.Collections.Generic;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.GUI.ViewModels;

namespace UnchainedLauncher.GUI.Views.DesignInstances {
    public static class LauncherViewModelInstances {
        public static LauncherVM DEFAULT => new LauncherVM(
            SettingsViewModelInstances.DEFAULT,
            null,
            null,
            null,
            null,
            new MessageBoxSpawner()
            );
    }
}