namespace UnchainedLauncher.UnrealModScanner.Scanning {
    public class AssetScannerOptions {
        public bool InvertCheck { get; set; }
        public List<string> AssetExtensions { get; set; }
        public List<string> AssetDirectories { get; set; }

        public AssetScannerOptions(List<string>? dirs = null, List<string>? exts = null, bool invert_check = false) {
            InvertCheck = invert_check;
            AssetExtensions = exts ?? new List<string> { ".uasset", ".umap" };
            AssetDirectories = dirs ?? [
                                        "Abilities",
                                        "AI",
                                        "Animation",
                                        "Audio",
                                        "Blueprint",
                                        "Characters",
                                        "Cinematics",
                                        "Collections",
                                        "Config",
                                        "Custom_Lens_Flare_VFX",
                                        "Customization",
                                        "Debug",
                                        "Developers",
                                        "Environments",
                                        "FX",
                                        "Game",
                                        "GameModes",
                                        "Gameplay",
                                        "Interactables",
                                        "Inventory",
                                        "Localization",
                                        "MapGen",
                                        "Maps",
                                        "MapsTest",
                                        "Materials",
                                        "Meshes",
                                        "Trailer_Cinematic",
                                        "UI",
                                        "Weapons",
                                        "Engine",
                                        "Mannequin",
                                    ];
        }
    }
}
