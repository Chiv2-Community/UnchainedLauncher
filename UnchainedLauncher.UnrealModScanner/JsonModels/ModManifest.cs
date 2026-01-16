
using Newtonsoft.Json;

namespace UnchainedLauncher.UnrealModScanner.JsonModels {
    public record ModManifest {
        [JsonProperty("schema_version")]
        public int SchemaVersion { get; set; } = 1;

        [JsonProperty("generated_at")]
        public string GeneratedAt { get; set; } = DateTime.UtcNow.ToString("u");

        [JsonProperty("scanner_version")]
        public string ScannerVersion { get; set; } = "3.3.1";

        // Master list of all scanned paks
        [JsonProperty("paks")]
        public List<PakInventoryDto> Paks { get; set; } = new();
    }

    public record PakInventoryDto {
        [JsonProperty("pak_name")]
        public string PakName { get; set; } = string.Empty;

        [JsonProperty("pak_path")]
        public string PakPath { get; set; } = string.Empty;

        [JsonProperty("pak_hash")]
        public string? PakHash { get; set; }

        [JsonProperty("inventory")]
        public AssetCollections Inventory { get; set; } = new();
    }

    public record AssetCollections {
        [JsonProperty("markers")]
        public List<ModMarkerDto> Markers { get; set; } = new();

        [JsonProperty("blueprints")]
        public List<BlueprintDto> Blueprints { get; set; } = new();

        [JsonProperty("maps")]
        public List<MapDto> Maps { get; set; } = new();

        [JsonProperty("replacements")]
        public List<ReplacementDto> Replacements { get; set; } = new();

        [JsonProperty("arbitrary")]
        public List<ArbitraryDto> Arbitrary { get; set; } = new();
    }


    public abstract record BaseAssetDto {
        [JsonProperty("path")]
        public string Path { get; set; } = string.Empty;

        [JsonProperty("hash")]
        public string Hash { get; set; } = string.Empty;

        [JsonProperty("class_path")]
        public string ClassPath { get; set; } = string.Empty;

        [JsonProperty("object_class")]
        public string? ObjectClass { get; set; }

        [JsonProperty("is_orphaned")]
        public bool? IsOrphaned { get; set; } = null;
    }

    public record ModMarkerDto : BaseAssetDto {
        [JsonProperty("associated_blueprints")]
        public List<string> AssociatedBlueprints { get; set; } = new();
    }

    public record BlueprintDto : BaseAssetDto {
        [JsonProperty("mod_name")]
        public string ModName { get; set; } = string.Empty;

        [JsonProperty("version")]
        public string Version { get; set; } = string.Empty;

        [JsonProperty("mod_description")]
        public string ModDescription { get; set; } = string.Empty;

        [JsonProperty("mod_repo_url")]
        public string ModRepoURL { get; set; } = string.Empty;

        [JsonProperty("author")]
        public string Author { get; set; } = string.Empty;

        [JsonProperty("silent_load")]
        public bool bSilentLoad { get; set; }

        [JsonProperty("show_in_gui")]
        public bool bShowInGUI { get; set; }

        [JsonProperty("is_client_side")]
        public bool bClientside { get; set; }

        [JsonProperty("online_only")]
        public bool bOnlineOnly { get; set; }

        [JsonProperty("host_only")]
        public bool bHostOnly { get; set; }

        [JsonProperty("allow_on_frontend")]
        public bool bAllowOnFrontend { get; set; }
    }

    public record MapDto : BaseAssetDto {
        [JsonProperty("game_mode")]
        public string? GameMode { get; set; }
        [JsonProperty("map_name")]
        public string? MapName { get; set; }
        [JsonProperty("map_description")]
        public string? MapDescription { get; set; }
        [JsonProperty("attacking_faction")]
        public string? AttackingFaction { get; set; }
        [JsonProperty("defending_faction")]
        public string? DefendingFaction { get; set; }
        [JsonProperty("game_mode_type")]
        public string? GamemodeType { get; set; }
        [JsonProperty("tbl_default_gamemode")]
        public string? TBLDefaultGameMode { get; set; }
    }

    // Base fields are sufficient
    public record ReplacementDto : BaseAssetDto;

    public record ArbitraryDto : BaseAssetDto {
        // Legacy / fallback metadata
        [JsonProperty("mod_name")]
        public string? ModName { get; set; }
    }
}