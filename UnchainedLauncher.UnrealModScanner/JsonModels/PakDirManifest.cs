
using Newtonsoft.Json;

namespace UnchainedLauncher.UnrealModScanner.JsonModels {
    public record PakDirManifest(
        [property: JsonProperty("paks")] List<PakManifest> Paks,
        [property: JsonProperty("schema_version")] int SchemaVersion = 1,
        [property: JsonProperty("generated_at")] DateTimeOffset GeneratedAt = default,
        [property: JsonProperty("scanner_version")] string ScannerVersion = "3.3.1"
    );

    public record PakManifest(
        [property: JsonProperty("pak_name")] string PakName,
        [property: JsonProperty("pak_path")] string PakPath,
        [property: JsonProperty("pak_hash")] string? PakHash,
        [property: JsonProperty("inventory")] AssetCollections Inventory
    );

    public record AssetCollections(
        [property: JsonProperty("markers")] List<ModMarkerDto> Markers,
        [property: JsonProperty("blueprints")] List<BlueprintDto> Blueprints,
        [property: JsonProperty("maps")] List<MapDto> Maps,
        [property: JsonProperty("replacements")] List<ReplacementDto> Replacements,
        [property: JsonProperty("arbitrary")] List<ArbitraryDto> Arbitrary
    );


    public abstract record BaseAssetDto(
        [property: JsonProperty("path")] string Path,
        [property: JsonProperty("hash")] string Hash,
        [property: JsonProperty("class_path")] string? ClassPath,
        [property: JsonProperty("object_class")] string? ObjectClass,
        [property: JsonProperty("is_orphaned")] bool? IsOrphaned = null
    );

    public record ModMarkerDto(
        string Path,
        string Hash,
        string ClassPath,
        string? ObjectClass,
        bool? IsOrphaned,
        [property: JsonProperty("associated_blueprints")] List<string> AssociatedBlueprints
    ) : BaseAssetDto(Path, Hash, ClassPath, ObjectClass, IsOrphaned);

    public record BlueprintDto(
        string Path,
        string Hash,
        string ClassPath,
        string? ObjectClass,
        bool? IsOrphaned,
        [property: JsonProperty("mod_name")] string ModName,
        [property: JsonProperty("version")] string Version,
        [property: JsonProperty("mod_description")] string ModDescription,
        [property: JsonProperty("mod_repo_url")] string ModRepoURL,
        [property: JsonProperty("author")] string Author,
        [property: JsonProperty("enable_by_default")] bool bEnableByDefault,
        [property: JsonProperty("silent_load")] bool bSilentLoad,
        [property: JsonProperty("show_in_gui")] bool bShowInGUI,
        [property: JsonProperty("is_client_side")] bool bClientside,
        [property: JsonProperty("online_only")] bool bOnlineOnly,
        [property: JsonProperty("host_only")] bool bHostOnly,
        [property: JsonProperty("allow_on_frontend")] bool bAllowOnFrontend,
        [property: JsonProperty("is_hidden")] bool? IsHidden
    ) : BaseAssetDto(Path, Hash, ClassPath, ObjectClass, IsOrphaned);

    public record MapDto(
        string Path,
        string Hash,
        string? ClassPath,
        string? ObjectClass,
        bool? IsOrphaned,
        [property: JsonProperty("game_mode")] string? GameMode,
        [property: JsonProperty("map_name")] string? MapName,
        [property: JsonProperty("map_description")] string? MapDescription,
        [property: JsonProperty("attacking_faction")] string? AttackingFaction,
        [property: JsonProperty("defending_faction")] string? DefendingFaction,
        [property: JsonProperty("game_mode_type")] string? GamemodeType,
        [property: JsonProperty("tbl_default_gamemode")] string? TBLDefaultGameMode
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
        [property: JsonProperty("mod_name")] string? ModName
    ) : BaseAssetDto(Path, Hash, ClassPath, ObjectClass, IsOrphaned);
}