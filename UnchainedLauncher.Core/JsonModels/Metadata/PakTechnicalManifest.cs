using System.Text.Json.Serialization;

namespace UnchainedLauncher.Core.JsonModels.Metadata {
    public record TechnicalManifest {
        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; set; } = 1;

        [JsonPropertyName("generated_at")]
        public string GeneratedAt { get; set; } = DateTime.UtcNow.ToString("u");

        [JsonPropertyName("scanner_version")]
        public string ScannerVersion { get; set; } = "3.3.1";

        // Master list of all scanned paks
        [JsonPropertyName("paks")]
        public List<PakInventoryDto> Paks { get; set; } = new();
    }

    public record PakInventoryDto {
        [JsonPropertyName("pak_name")]
        public string PakName { get; set; } = string.Empty;

        [JsonPropertyName("pak_path")]
        public string PakPath { get; set; } = string.Empty;

        [JsonPropertyName("pak_hash")]
        public string? PakHash { get; set; }

        [JsonPropertyName("inventory")]
        public AssetCollections Inventory { get; set; } = new();
    }

    public record AssetCollections {
        [JsonPropertyName("markers")]
        public List<ModMarkerDto> Markers { get; set; } = new();

        [JsonPropertyName("blueprints")]
        public List<BlueprintDto> Blueprints { get; set; } = new();

        [JsonPropertyName("maps")]
        public List<MapDto> Maps { get; set; } = new();

        [JsonPropertyName("replacements")]
        public List<ReplacementDto> Replacements { get; set; } = new();

        [JsonPropertyName("arbitrary")]
        public List<ArbitraryDto> Arbitrary { get; set; } = new();
    }

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "asset_type")]
    [JsonDerivedType(typeof(ModMarkerDto), "marker")]
    [JsonDerivedType(typeof(BlueprintDto), "blueprint")]
    [JsonDerivedType(typeof(MapDto), "map")]
    [JsonDerivedType(typeof(ReplacementDto), "replacement")]
    [JsonDerivedType(typeof(ArbitraryDto), "arbitrary")]
    public abstract record BaseAssetDto {
        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        [JsonPropertyName("hash")]
        public string Hash { get; set; } = string.Empty;

        [JsonPropertyName("object_class")]
        public string? ObjectClass { get; set; }
    }

    public record ModMarkerDto : BaseAssetDto {
        [JsonPropertyName("associated_blueprints")]
        public List<string> AssociatedBlueprints { get; set; } = new();
    }

    public record BlueprintDto : BaseAssetDto {
        [JsonPropertyName("mod_name")]
        public string ModName { get; set; } = string.Empty;

        [JsonPropertyName("author")]
        public string Author { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("is_client_side")]
        public bool IsClientSide { get; set; }
    }

    public record MapDto : BaseAssetDto {
        [JsonPropertyName("game_mode")]
        public string? GameMode { get; set; }
    }

    // Base fields are sufficient
    public record ReplacementDto : BaseAssetDto;

    public record ArbitraryDto : BaseAssetDto {
        // Legacy / fallback metadata
        [JsonPropertyName("mod_name")]
        public string? ModName { get; set; }
    }
}