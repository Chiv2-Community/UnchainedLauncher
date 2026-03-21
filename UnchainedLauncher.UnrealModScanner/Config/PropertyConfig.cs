

namespace UnchainedLauncher.UnrealModScanner.Config {
    /// <summary>
    /// Limits recursion depth when using EExtractionMode.Json. 
    /// 0 = Root only, 1 = One level deep, etc.
    /// </summary>
    public record PropertyConfig(
        string Name,
        EExtractionMode Mode,
        int MaxDepth = 3
    );

    public record CdoProcessorConfig(
        string TargetClassName = "/Game/Mods/ArgonSDK/Mods/ArgonSDKModBase",
        List<PropertyConfig>? Properties = null
    ) {
        public List<PropertyConfig> Properties { get; init; } = Properties ?? new List<PropertyConfig> {
            new("ModName", EExtractionMode.String),
            new("ModVersion", EExtractionMode.String),
            new("ModDescription", EExtractionMode.String),
            new("ModRepoURL", EExtractionMode.String),
            new("Author", EExtractionMode.String),
            new("bEnableByDefault", EExtractionMode.Raw),
            new("bSilentLoad", EExtractionMode.Raw),
            new("bShowInGUI ", EExtractionMode.Raw),
            new("bClientside", EExtractionMode.Raw),
            new("bOnlineOnly", EExtractionMode.Raw),
            new("bHostOnly", EExtractionMode.Raw),
            new("bAllowOnFrontend", EExtractionMode.Raw),
        };
    }

    public record MarkerDiscoveryConfig(
        string MarkerClassName = "DA_ModMarker_C",
        string MapPropertyName = "ModActors",
        List<PropertyConfig>? ReferencedBlueprintProperties = null
    ) {
        // Name of TMap Property which holds references to mod assets
        // Which properties to grab from the CDO of the referenced blueprints
        public List<PropertyConfig> ReferencedBlueprintProperties { get; init; } = ReferencedBlueprintProperties ?? new List<PropertyConfig> {
            new("ModName", EExtractionMode.String),
            new("ModVersion", EExtractionMode.String),
            new("ModDescription", EExtractionMode.String),
            new("ModRepoURL", EExtractionMode.String),
            new("Author", EExtractionMode.String),
            new("bEnableByDefault", EExtractionMode.Raw),
            new("bSilentLoad", EExtractionMode.Raw),
            new("bShowInGUI ", EExtractionMode.Raw),
            new("bClientside", EExtractionMode.Raw),
            new("bOnlineOnly", EExtractionMode.Raw),
            new("bHostOnly", EExtractionMode.Raw),
            new("bAllowOnFrontend", EExtractionMode.Raw),
        };
    }
}