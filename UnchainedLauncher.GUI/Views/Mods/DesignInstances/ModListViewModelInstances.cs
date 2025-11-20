using System.Collections.Generic;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.GUI.ViewModels;
using UnchainedLauncher.GUI.Views.DesignInstances;

namespace UnchainedLauncher.GUI.Views.Mods.DesignInstances {
    public static class ModListViewModelInstances {
        public static ModListVM DEFAULT => new ModListDesignVM();

        // TODO: Create some kind of InMemory ModRegistry for design/testing purposes
        public static IModManager DEFAULTMODMANAGER => new ModManager(
            new LocalModRegistry("design-view"),
            new List<ReleaseCoordinates> { ReleaseCoordinates.FromRelease(ModViewModelInstances.DesignViewRelease) }
        );
    }

    public class ModListDesignVM : ModListVM {
        public ModListDesignVM() : base(
            ModListViewModelInstances.DEFAULTMODMANAGER,
            new MessageBoxSpawner()
        ) {
            SelectedMod = ModViewModelInstances.DEFAULT;
            DisplayMods.Add(ModViewModelInstances.DEFAULT);
        }
    }
}