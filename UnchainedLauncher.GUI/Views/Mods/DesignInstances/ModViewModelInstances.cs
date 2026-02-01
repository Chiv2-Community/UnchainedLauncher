using LanguageExt;
using System;
using System.Collections.Generic;
using System.Linq;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.GUI.ViewModels;

namespace UnchainedLauncher.GUI.Views.Mods.DesignInstances {
    using static Prelude;

    public static class ModViewModelInstances {
        public static readonly ModManifest DesignViewManifest = new(
            "https://github.com/Gooner/FinallyMod",
            "FinallyMod",
            "It has finally been done",
            "https://example.com",
            "https://avatars.githubusercontent.com/u/108312122?s=96&v=4",
            ModType.Shared,
            new List<string> { "Finally", "Gooner" },
            new List<Dependency> { new("https://Gooner/BaseMod", "v1.0.0") },
            new List<ModTag> { ModTag.Cosmetic },
            new List<string> { "TDM_Dungeon" },
            new OptionFlags(false)
        );

        public static readonly Release DesignViewRelease = new("v1.0.0", "abcd", "ExamplePak", DateTime.Now,
            DesignViewManifest, "## Example Release Notes\n\n* Foo\n* bar\n* baz");

        public static readonly Core.JsonModels.Metadata.V3.Mod DesignViewMod = new(
            DesignViewManifest,
            new List<Release> { DesignViewRelease }
        );

        public static ModVM DEFAULT => new ModDesignVM();
    }

    public class ModDesignVM : ModVM {
        public ModDesignVM() : base(
            ModViewModelInstances.DesignViewMod,
            Some(ModViewModelInstances.DesignViewRelease),
            new ModManager(
                new LocalModRegistry("foo"),
                new PakDir(FilePaths.PakDir, Enumerable.Empty<ManagedPak>()),
                new List<ReleaseCoordinates>())
        ) {
        }
    }
}