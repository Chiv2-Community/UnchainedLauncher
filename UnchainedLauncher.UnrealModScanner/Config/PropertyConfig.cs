

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
        public string TargetClassName { get; set; } = "/Game/Mods/ArgonSDK/Mods/ArgonSDKModBase";
        public List<PropertyConfig> Properties { get; set; } = new List<PropertyConfig> {
                            new() { Name = "ModName", Mode = EExtractionMode.String },
                            new() { Name = "ModVersion", Mode = EExtractionMode.String },
                            new() { Name = "ModDescription", Mode = EExtractionMode.String },
                            new() { Name = "ModRepoURL", Mode = EExtractionMode.String },
                            new() { Name = "Author", Mode = EExtractionMode.String },
                            new() { Name = "bEnableByDefault", Mode = EExtractionMode.Raw },
                            new() { Name = "bSilentLoad", Mode = EExtractionMode.Raw },
                            new() { Name = "bShowInGUI ", Mode = EExtractionMode.Raw },
                            new() { Name = "bClientside", Mode = EExtractionMode.Raw },
                            new() { Name = "bOnlineOnly", Mode = EExtractionMode.Raw },
                            new() { Name = "bHostOnly", Mode = EExtractionMode.Raw },
                            new() { Name = "bAllowOnFrontend", Mode = EExtractionMode.Raw },
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
                            new() { Name = "ModDescription", Mode = EExtractionMode.String },
                            new() { Name = "ModRepoURL", Mode = EExtractionMode.String },
                            new() { Name = "Author", Mode = EExtractionMode.String },
                            new() { Name = "bEnableByDefault", Mode = EExtractionMode.Raw },
                            new() { Name = "bSilentLoad", Mode = EExtractionMode.Raw },
                            new() { Name = "bShowInGUI ", Mode = EExtractionMode.Raw },
                            new() { Name = "bClientside", Mode = EExtractionMode.Raw },
                            new() { Name = "bOnlineOnly", Mode = EExtractionMode.Raw },
                            new() { Name = "bHostOnly", Mode = EExtractionMode.Raw },
                            new() { Name = "bAllowOnFrontend", Mode = EExtractionMode.Raw },
                        };
    }
}