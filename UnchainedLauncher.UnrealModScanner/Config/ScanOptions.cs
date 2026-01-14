using CUE4Parse.UE4.Versions;


namespace UnchainedLauncher.UnrealModScanner.Config {
    public class ScanOptions {
        public EGame GameVersion { get; set; } = EGame.GAME_UE4_25;
        public string AesKey { get; set; } = "0x0000000000000000000000000000000000000000000000000000000000000000";
        /// <summary>
        /// List of path substrings to match (e.g., "/Game/Maps/", "Engine/").
        /// </summary>
        public List<string> PathFilters { get; set; } = [
            "Abilities","AI","Animation","Audio","Blueprint","Characters","Cinematics",
            "Collections","Config","Custom_Lens_Flare_VFX","Customization","Debug",
            "Developers","Environments","FX","Game","GameModes","Gameplay","Interactables",
            "Inventory","Localization","MapGen","Maps","MapsTest","Materials","Meshes",
            "Trailer_Cinematic","UI","Weapons","Engine","Mannequin"
        ];

        /// <summary>
        /// If true, ONLY scan paths matching filters. If false, SKIP paths matching filters.
        /// </summary>
        public bool IsWhitelist { get; set; } = false;
        public List<CdoProcessorConfig> CdoProcessors { get; set; } = new() { new CdoProcessorConfig() };
        public List<MarkerDiscoveryConfig> MarkerProcessors { get; set; } = new() { new MarkerDiscoveryConfig() };
        public List<ProcessorTarget> Targets { get; set; } = new(); // NYI
    }

    public class ProcessorTarget {
        public string ClassName { get; set; } = ""; // e.g., "Default__MyMarker_C"
        public bool DumpAllProperties { get; set; } = false; // If true, ignore 'Properties' list and dump everything
        public List<PropertyConfig> Properties { get; set; } = new();
    }
}
