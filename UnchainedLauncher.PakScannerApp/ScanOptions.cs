using UnchainedLauncher.UnrealModScanner.PakScanning;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;

namespace UnchainedLauncher.PakScannerApp {

    public sealed class ScanOptions {
        public string PakDirectory { get; set; } = "";
        public string? OutputDirectory { get; set; }
        public ScanMode ScanMode { get; set; } = ScanMode.ModsOnly;
        public string[] OfficialDirectories { get; set; } = DefaultOfficialDirs;

        public static readonly string[] DefaultOfficialDirs =
        {
            "Abilities","AI","Animation","Audio","Blueprint","Characters","Cinematics",
            "Collections","Config","Custom_Lens_Flare_VFX","Customization","Debug",
            "Developers","Environments","FX","Game","GameModes","Gameplay","Interactables",
            "Inventory","Localization","MapGen","Maps","MapsTest","Materials","Meshes",
            "Trailer_Cinematic","UI","Weapons","Engine","Mannequin"
        };
    }




}