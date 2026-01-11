using LanguageExt;
using System;
using System.Collections.Generic;
using UnchainedLauncher.Core.JsonModels.Metadata.V4;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.GUI.ViewModels;

namespace UnchainedLauncher.GUI.Views.Mods.DesignInstances {
    using static Prelude;

    public static class ModViewModelInstances {
        public static readonly Components DesignViewComponents = new(
            Actors: new List<Actor> {
                new("Custom Sword", "A legendary blade with custom animations", "/Game/Weapons/Swords/CustomSword", false, 
                    new List<string> { "https://example.com/sword1.png", "https://example.com/sword2.png" }),
                new("Fancy Hat", "Purely cosmetic headgear", "/Game/Cosmetics/Hats/FancyHat", true,
                    new List<string> { "https://example.com/hat.png" })
            },
            Maps: new List<Chivalry2Map> {
                new("Arena Deathmatch", "A custom arena for intense battles", "/Game/Maps/CustomArena",
                    new List<string> { "https://example.com/arena1.png", "https://example.com/arena2.png" }),
                new("Castle Siege", "Epic siege warfare map", "/Game/Maps/CastleSiege",
                    new List<string> { "https://example.com/castle.png" })
            },
            Replacements: new Replacements(
                "Texture Overhaul",
                "Replaces default textures with high-resolution versions",
                new List<string> { "/Game/Textures/Weapons", "/Game/Textures/Armor" },
                new List<string> { "https://example.com/texture1.png", "https://example.com/texture2.png" }
            )
        );

        public static readonly ModManifest DesignViewManifest = new(
            "https://github.com/Gooner/FinallyMod",
            "FinallyMod",
            "It has finally been done",
            "https://example.com",
            "https://avatars.githubusercontent.com/u/108312122?s=96&v=4",
            new List<string> { "Finally", "Gooner" },
            new List<Dependency> { new("https://Gooner/BaseMod", "v1.0.0") },
            DesignViewComponents
        );

        public static readonly Release DesignViewRelease = new("v1.0.0", "abcd", "ExamplePak", DateTime.Now,
            DesignViewManifest, "## Example Release Notes\n\n* Foo\n* bar\n* baz");

        public static readonly Core.JsonModels.Metadata.V4.Mod DesignViewMod = new(
            DesignViewManifest,
            new List<Release> { DesignViewRelease }
        );

        public static ModVM DEFAULT => new ModDesignVM();
    }

    public class ModDesignVM : ModVM {
        public ModDesignVM() : base(
            ModViewModelInstances.DesignViewMod,
            Some(ModViewModelInstances.DesignViewRelease),
            new ModManager(new LocalModRegistry("foo"), new List<ReleaseCoordinates>())
        ) {
        }
    }
}