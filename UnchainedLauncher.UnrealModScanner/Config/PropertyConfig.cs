

namespace UnchainedLauncher.UnrealModScanner.Config {
    public class PropertyConfig {
        public string Name { get; set; } = "ModName";
        public EExtractionMode Mode { get; set; } = EExtractionMode.String;
        /// <summary>
        /// Limits recursion depth when using EExtractionMode.Json. 
        /// 0 = Root only, 1 = One level deep, etc.
        /// </summary>
        public int MaxDepth { get; set; } = 3;
    }

    public class CdoProcessorConfig {
        //public string TargetClassName { get; set; } = "TBL/Content/Mods/ArgonSDK/Mods/ArgonSDKModBase.ArgonSDKModBase_C";
        public string TargetClassName { get; set; } = "DA_ModMarker";
        public List<PropertyConfig> Properties { get; set; } = new List<PropertyConfig> {
                            new() { Name = "ModActors", Mode = EExtractionMode.Json },
                            new() { Name = "CustomObjects", Mode = EExtractionMode.Json },
                        };
    }

    public class MarkerDiscoveryConfig {
        public string MarkerClassName { get; set; } = "DA_ModMarker_C";
        // Name of TMap Property which holds references to mod assets
        public string MapPropertyName { get; set; } = "ModActors";
        // Which properties to grab from the CDO of the referenced blueprints
        public List<PropertyConfig> ReferencedBlueprintProperties { get; set; } = new List<PropertyConfig> {
                            new() { Name = "ModName", Mode = EExtractionMode.String },
                            new() { Name = "ModVersion", Mode = EExtractionMode.String },
                            new() { Name = "Author", Mode = EExtractionMode.String },
                            new() { Name = "bClientside", Mode = EExtractionMode.Raw },
                        };
    }
}
