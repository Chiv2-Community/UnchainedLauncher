using LanguageExt;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Mods;
using UnchainedLauncher.Core.Mods.Registry;
using UnchainedLauncher.Core.Mods.Registry.Downloader;
using UnchainedLauncher.GUI.ViewModels;

namespace UnchainedLauncher.GUI.Views.DesignInstances {
    public static class ModListViewModelInstances {
        public static ModListViewModel DEFAULT => CreateDefaultModListViewModel();

        private static ModListViewModel CreateDefaultModListViewModel() {
            var instance = new ModListViewModel(
                new ModManager(
                    new HashMap<IModRegistry, IEnumerable<Mod>> {
                        { new LocalModRegistry("", new LocalFilePakDownloader("")), new List<Mod> { ModViewModelInstances.DesignViewMod } }
                    },
                    new List<Release> { ModViewModelInstances.DesignViewRelease }
                )
            );

            instance.SelectedMod = ModViewModelInstances.DEFAULT;
            instance.DisplayMods.Add(ModViewModelInstances.DEFAULT);

            return instance;
        }
    }
}