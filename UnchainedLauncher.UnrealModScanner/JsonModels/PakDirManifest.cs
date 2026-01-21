
using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace UnchainedLauncher.UnrealModScanner.JsonModels {
    public record PakDirManifest(
        [property: JsonPropertyName("paks")][property: JsonProperty("paks")] List<PakManifest> Paks,
        [property: JsonPropertyName("schema_version")][property: JsonProperty("schema_version")] int SchemaVersion = 1,
        [property: JsonPropertyName("generated_at")][property: JsonProperty("generated_at")] DateTimeOffset GeneratedAt = default,
        [property: JsonPropertyName("scanner_version")][property: JsonProperty("scanner_version")] string ScannerVersion = "3.3.1"
    );

    public record PakManifest(
        [property: JsonPropertyName("pak_name")][property: JsonProperty("pak_name")] string PakName,
        [property: JsonPropertyName("pak_path")][property: JsonProperty("pak_path")] string PakPath,
        [property: JsonPropertyName("pak_hash")][property: JsonProperty("pak_hash")] string? PakHash,
        [property: JsonPropertyName("inventory")][property: JsonProperty("inventory")] AssetCollections Inventory
    );

    public record AssetCollections(
        [property: JsonPropertyName("markers")][property: JsonProperty("markers")] List<ModMarkerDto> Markers,
        [property: JsonPropertyName("blueprints")][property: JsonProperty("blueprints")] List<BlueprintDto> Blueprints,
        [property: JsonPropertyName("maps")][property: JsonProperty("maps")] List<MapDto> Maps,
        [property: JsonPropertyName("replacements")][property: JsonProperty("replacements")] List<ReplacementDto> Replacements,
        [property: JsonPropertyName("arbitrary")][property: JsonProperty("arbitrary")] List<ArbitraryDto> Arbitrary
    );


    public abstract record BaseAssetDto(
        [property: JsonPropertyName("path")][property: JsonProperty("path")] string Path,
        [property: JsonPropertyName("hash")][property: JsonProperty("hash")] string Hash,
        [property: JsonPropertyName("class_path")][property: JsonProperty("class_path")] string? ClassPath,
        [property: JsonPropertyName("object_class")][property: JsonProperty("object_class")] string? ObjectClass,
        [property: JsonPropertyName("is_orphaned")][property: JsonProperty("is_orphaned")] bool? IsOrphaned = null
    );

    public record ModMarkerDto(
        string Path,
        string Hash,
        string? ClassPath,
        string? ObjectClass,
        bool? IsOrphaned,
        [property: JsonPropertyName("associated_blueprints")][property: JsonProperty("associated_blueprints")] List<string> AssociatedBlueprints
    ) : BaseAssetDto(Path, Hash, ClassPath, ObjectClass, IsOrphaned);

    public record BlueprintDto(
        string Path,
        string Hash,
        string? ClassPath,
        string? ObjectClass,
        bool? IsOrphaned,
        [property: JsonPropertyName("mod_name")][property: JsonProperty("mod_name")] string ModName,
        [property: JsonPropertyName("version")][property: JsonProperty("version")] string Version,
        [property: JsonPropertyName("mod_description")][property: JsonProperty("mod_description")] string ModDescription,
        [property: JsonPropertyName("mod_repo_url")][property: JsonProperty("mod_repo_url")] string ModRepoURL,
        [property: JsonPropertyName("author")][property: JsonProperty("author")] string Author,
        [property: JsonPropertyName("enable_by_default")][property: JsonProperty("enable_by_default")] bool bEnableByDefault,
        [property: JsonPropertyName("silent_load")][property: JsonProperty("silent_load")] bool bSilentLoad,
        [property: JsonPropertyName("show_in_gui")][property: JsonProperty("show_in_gui")] bool bShowInGUI,
        [property: JsonPropertyName("is_client_side")][property: JsonProperty("is_client_side")] bool bClientside,
        [property: JsonPropertyName("online_only")][property: JsonProperty("online_only")] bool bOnlineOnly,
        [property: JsonPropertyName("host_only")][property: JsonProperty("host_only")] bool bHostOnly,
        [property: JsonPropertyName("allow_on_frontend")][property: JsonProperty("allow_on_frontend")] bool bAllowOnFrontend,
        [property: JsonPropertyName("is_hidden")][property: JsonProperty("is_hidden")] bool? IsHidden
    ) : BaseAssetDto(Path, Hash, ClassPath, ObjectClass, IsOrphaned);

    public record MapDto(
        string Path,
        string Hash,
        string? ClassPath,
        string? ObjectClass,
        bool? IsOrphaned,
        [property: JsonPropertyName("game_mode")][property: JsonProperty("game_mode")] string? GameMode,
        [property: JsonPropertyName("map_name")][property: JsonProperty("map_name")] string? MapName,
        [property: JsonPropertyName("map_description")][property: JsonProperty("map_description")] string? MapDescription,
        [property: JsonPropertyName("attacking_faction")][property: JsonProperty("attacking_faction")] string? AttackingFaction,
        [property: JsonPropertyName("defending_faction")][property: JsonProperty("defending_faction")] string? DefendingFaction,
        [property: JsonPropertyName("game_mode_type")][property: JsonProperty("game_mode_type")] string? GamemodeType,
        [property: JsonPropertyName("tbl_default_gamemode")][property: JsonProperty("tbl_default_gamemode")] string? TBLDefaultGameMode
    ) : BaseAssetDto(Path, Hash, ClassPath, ObjectClass, IsOrphaned);

    // Base fields are sufficient
    public record ReplacementDto(
        string Path,
        string Hash,
        string? ClassPath,
        string? ObjectClass,
        bool? IsOrphaned
    ) : BaseAssetDto(Path, Hash, ClassPath, ObjectClass, IsOrphaned);

    public record ArbitraryDto(
        string Path,
        string Hash,
        string? ClassPath,
        string? ObjectClass,
        bool? IsOrphaned,
        [property: JsonPropertyName("mod_name")][property: JsonProperty("mod_name")] string? ModName
    ) : BaseAssetDto(Path, Hash, ClassPath, ObjectClass, IsOrphaned);
}