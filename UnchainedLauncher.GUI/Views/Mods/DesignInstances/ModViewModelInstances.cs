using LanguageExt;
using System;
using System.Collections.Generic;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.PakDir;
using UnchainedLauncher.GUI.ViewModels;

namespace UnchainedLauncher.GUI.Views.Mods.DesignInstances {
    using static Prelude;

    public static class ModViewModelInstances {
        public static readonly ModInfo DesignViewModInfo = new(
            "https://github.com/Gooner/FinallyMod",
            "FinallyMod",
            "It has finally been done",
            "https://example.com",
            "https://avatars.githubusercontent.com/u/108312122?s=96&v=4",
            new List<string>() { "https://avatars.githubusercontent.com/u/108312122?s=96&v=4" },
            new List<string> { "Finally", "Gooner" },
            new List<Dependency> { new("https://Gooner/BaseMod", "v1.0.0") }
        );

        public static readonly Release DesignViewRelease = new("v1.0.0", "abcd", "ExamplePak", DateTime.Now,
            DesignViewModInfo, null, "## Example Release Notes\n\n* Foo\n* bar\n* baz");

        public static readonly Core.JsonModels.ModMetadata.Mod DesignViewMod = new(
            DesignViewModInfo,
            new List<Release> { DesignViewRelease }
        );

        public static ModVM DEFAULT => new ModDesignVM();
    }

    public class ModDesignVM : ModVM {
        public ModDesignVM() : base(
            ModViewModelInstances.DesignViewMod,
            Some(ModViewModelInstances.DesignViewRelease),
            new ModManager(new LocalModRegistry("foo"), new List<ReleaseCoordinates>()),
            new PakDir("design-view-pak")
        ) {
        }
    }
}