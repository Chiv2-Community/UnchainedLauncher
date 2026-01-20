
using System.Text.Json.Serialization;

namespace UnchainedLauncher.UnrealModScanner.JsonModels {
    public record PakDirManifest(
        [property: JsonPropertyName("paks")] List<PakManifest> Paks,
        [property: JsonPropertyName("schema_version")] int SchemaVersion = 1,
        [property: JsonPropertyName("generated_at")] DateTimeOffset GeneratedAt = default,
        [property: JsonPropertyName("scanner_version")] string ScannerVersion = "3.3.1"
    );

    public record PakManifest(
        [property: JsonPropertyName("pak_name")] string PakName,
        [property: JsonPropertyName("pak_path")] string PakPath,
        [property: JsonPropertyName("pak_hash")] string? PakHash,
        [property: JsonPropertyName("inventory")] AssetCollections Inventory
    );

    public record AssetCollections(
        [property: JsonPropertyName("markers")] List<ModMarkerDto> Markers,
        [property: JsonPropertyName("blueprints")] List<BlueprintDto> Blueprints,
        [property: JsonPropertyName("maps")] List<MapDto> Maps,
        [property: JsonPropertyName("replacements")] List<ReplacementDto> Replacements,
        [property: JsonPropertyName("arbitrary")] List<ArbitraryDto> Arbitrary
    );


    public abstract record BaseAssetDto(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("hash")] string Hash,
        [property: JsonPropertyName("class_path")] string? ClassPath,
        [property: JsonPropertyName("object_class")] string? ObjectClass,
        [property: JsonPropertyName("is_orphaned")] bool? IsOrphaned = null
    );

    public record ModMarkerDto(
        string Path,
        string Hash,
        string ClassPath,
        string? ObjectClass,
        bool? IsOrphaned,
        [property: JsonPropertyName("associated_blueprints")] List<string> AssociatedBlueprints
    ) : BaseAssetDto(Path, Hash, ClassPath, ObjectClass, IsOrphaned);

    public record BlueprintDto(
        string Path,
        string Hash,
        string ClassPath,
        string? ObjectClass,
        bool? IsOrphaned,
        [property: JsonPropertyName("mod_name")] string ModName,
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("mod_description")] string ModDescription,
        [property: JsonPropertyName("mod_repo_url")] string ModRepoURL,
        [property: JsonPropertyName("author")] string Author,
        [property: JsonPropertyName("enable_by_default")] bool bEnableByDefault,
        [property: JsonPropertyName("silent_load")] bool bSilentLoad,
        [property: JsonPropertyName("show_in_gui")] bool bShowInGUI,
        [property: JsonPropertyName("is_client_side")] bool bClientside,
        [property: JsonPropertyName("online_only")] bool bOnlineOnly,
        [property: JsonPropertyName("host_only")] bool bHostOnly,
        [property: JsonPropertyName("allow_on_frontend")] bool bAllowOnFrontend,
        [property: JsonPropertyName("is_hidden")] bool? IsHidden
    ) : BaseAssetDto(Path, Hash, ClassPath, ObjectClass, IsOrphaned);

    public record MapDto(
        string Path,
        string Hash,
        string? ClassPath,
        string? ObjectClass,
        bool? IsOrphaned,
        [property: JsonPropertyName("game_mode")] string? GameMode,
        [property: JsonPropertyName("map_name")] string? MapName,
        [property: JsonPropertyName("map_description")] string? MapDescription,
        [property: JsonPropertyName("attacking_faction")] string? AttackingFaction,
        [property: JsonPropertyName("defending_faction")] string? DefendingFaction,
        [property: JsonPropertyName("game_mode_type")] string? GamemodeType,
        [property: JsonPropertyName("tbl_default_gamemode")] string? TBLDefaultGameMode
    ) : BaseAssetDto(Path, Hash, ClassPath, ObjectClass, IsOrphaned);

    // Base fields are sufficient
    public record ReplacementDto(
        string Path,
        string Hash,
        string ClassPath,
        string? ObjectClass,
        bool? IsOrphaned
    ) : BaseAssetDto(Path, Hash, ClassPath, ObjectClass, IsOrphaned);

    public record ArbitraryDto(
        string Path,
        string Hash,
        string ClassPath,
        string? ObjectClass,
        bool? IsOrphaned,
        [property: JsonPropertyName("mod_name")] string? ModName
    ) : BaseAssetDto(Path, Hash, ClassPath, ObjectClass, IsOrphaned);
}