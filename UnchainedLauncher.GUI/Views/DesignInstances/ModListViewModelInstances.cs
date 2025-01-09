using LanguageExt;
using System.Collections.Generic;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Mods.Registry.Downloader;
using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.GUI.ViewModels;

namespace UnchainedLauncher.GUI.Views.DesignInstances {
    public static class ModListViewModelInstances {
        public static ModListVM DEFAULT => CreateDefaultModListViewModel();

        private static ModListVM CreateDefaultModListViewModel() {
            var instance = new ModListVM(
                new ModManager(
                    new HashMap<IModRegistry, IEnumerable<Core.JsonModels.Metadata.V3.Mod>> {
                        { new LocalModRegistry("", new LocalFilePakDownloader("")), new List<Core.JsonModels.Metadata.V3.Mod> { ModViewModelInstances.DesignViewMod } }
                    },
                    new List<Release> { ModViewModelInstances.DesignViewRelease }
                ),
                new MessageBoxSpawner()
            );

            instance.SelectedMod = ModViewModelInstances.DEFAULT;
            instance.DisplayMods.Add(ModViewModelInstances.DEFAULT);

            return instance;
        }
    }
}