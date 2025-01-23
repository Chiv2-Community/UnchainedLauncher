using System.Collections.Generic;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Mods.Registry.Downloader;
using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.GUI.ViewModels;

namespace UnchainedLauncher.GUI.Views.DesignInstances {
    public static class ModListViewModelInstances {
        public static ModListVM DEFAULT => CreateDefaultModListViewModel();

        // TODO: Create some kind of InMemory ModRegistry for design/testing purposes
        public static IModManager DEFAULTMODMANAGER => new ModManager(
            new LocalModRegistry("", new LocalFilePakDownloader("")),
            new List<ReleaseCoordinates> { ReleaseCoordinates.FromRelease(ModViewModelInstances.DesignViewRelease) }
        );

        private static ModListVM CreateDefaultModListViewModel() {
            var instance = new ModListVM(
                DEFAULTMODMANAGER,
                new MessageBoxSpawner()
            );

            instance.SelectedMod = ModViewModelInstances.DEFAULT;
            instance.DisplayMods.Add(ModViewModelInstances.DEFAULT);

            return instance;
        }
    }
}