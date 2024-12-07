using LanguageExt;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Mods;
using UnchainedLauncher.Core.Mods.Registry;
using UnchainedLauncher.GUI.ViewModels;

namespace UnchainedLauncher.GUI.Views.DesignInstances {
    using static LanguageExt.Prelude;

    public static class ModViewModelInstances {
        public static ModViewModel DEFAULT => CreateDefaultModViewModel();

        public static readonly ModManifest DesignViewManifest = new ModManifest(
            "https://github.com/Gooner/FinallyMod",
            "FinallyMod",
            "It has finally been done",
            "https://example.com",
            "https://avatars.githubusercontent.com/u/108312122?s=96&v=4",
            ModType.Shared,
            new List<string> { "Finally", "Gooner" },
            new List<Dependency> {
                        new Dependency("https://Gooner/BaseMod", "v1.0.0")
            },
            new List<ModTag> { ModTag.Cosmetic },
            new List<string> { "TDM_Dungeon" },
            new OptionFlags(false)
        );

        public static readonly Release DesignViewRelease = new Release("v1.0.0", "abcd", "ExamplePak", DateTime.Now, DesignViewManifest);

        public static readonly Mod DesignViewMod = new Mod(
            DesignViewManifest,
            new List<Release> {
                DesignViewRelease
            }
        );

        private static ModViewModel CreateDefaultModViewModel() {
            return new ModViewModel(
                DesignViewMod,
                Some(DesignViewRelease),
                new ModManager(new HashMap<IModRegistry, IEnumerable<Mod>>(), new List<Release>())
            );
        }


    }
}