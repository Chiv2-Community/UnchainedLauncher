
using UnchainedLauncher.UnrealModScanner.PakScanning;
using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.Models {

    public record TechnicalManifest {
        public string GeneratedAt { get; set; } = DateTime.UtcNow.ToString("u");
        public string ScannerVersion { get; set; } = "3.3.1";

        // The master list of all paks scanned in this session
        public List<PakInventoryDto> Paks { get; set; } = new();
    }

    public record PakInventoryDto {
        public string PakName { get; set; } = string.Empty; // The Dict Key
        public string PakPath { get; set; } = string.Empty; // The Full Path
        public string? PakHash { get; set; }

        public AssetCollections Inventory { get; set; } = new();
    }

    public record AssetCollections {
        public List<ModMarkerDto> Markers { get; set; } = new();
        public List<BlueprintDto> Blueprints { get; set; } = new();
        public List<MapDto> Maps { get; set; } = new();
        public List<ReplacementDto> Replacements { get; set; } = new();
        public List<ArbitraryDto> Arbitrary { get; set; } = new();
    }

    // The Base class for all assets
    public abstract record BaseAssetDto {
        public string Path { get; set; } = string.Empty;
        public string Hash { get; set; } = string.Empty;
        public string? ObjectClass { get; set; }
    }

    public record ModMarkerDto : BaseAssetDto {
        public List<string> AssociatedBlueprints { get; set; } = new();
    }

    public record BlueprintDto : BaseAssetDto {
        public string ModName { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public bool IsClientSide { get; set; }
    }

    public record MapDto : BaseAssetDto {
        public string? GameMode { get; set; }
    }

    public record ReplacementDto : BaseAssetDto; // Just the base info is enough

    public record ArbitraryDto : BaseAssetDto {
        public string? ModName { get; set; } // Legacy fallback
    }

    public static class MetadataProcessor {
        public static TechnicalManifest ProcessModScan(ModScanResult scanResult) {
            var manifest = new TechnicalManifest();

            foreach (var entry in scanResult.Paks) {
                string pakName = entry.Key;
                PakScanResult pakData = entry.Value;

                // Use the Mapper logic to build the individual Pak DTO
                var pakInventory = new PakInventoryDto {
                    PakName = pakName,
                    PakPath = pakData.PakPath,
                    PakHash = pakData.PakHash,
                    Inventory = MapAssets(pakData)
                };

                manifest.Paks.Add(pakInventory);
            }

            return manifest;
        }

        private static AssetCollections MapAssets(PakScanResult result) {
            var collections = new AssetCollections();

            // 1. Process Markers & Blueprints
            foreach (var marker in result._Markers) {
                collections.Markers.Add(new ModMarkerDto {
                    Path = marker.MarkerAssetPath,
                    Hash = marker.MarkerAssetHash,
                    ObjectClass = "ModMarker",
                    AssociatedBlueprints = marker.Blueprints.Select(b => b.BlueprintPath).ToList()
                });

                foreach (var bp in marker.Blueprints) {
                    if (!collections.Blueprints.Any(x => x.Path == bp.BlueprintPath)) {
                        collections.Blueprints.Add(new BlueprintDto {
                            Path = bp.BlueprintPath,
                            Hash = bp.BlueprintHash,
                            ModName = bp.ModName,
                            Author = bp.Author,
                            Version = bp.Version,
                            IsClientSide = bp.IsClientSide,
                            ObjectClass = "BlueprintGeneratedClass"
                        });
                    }
                }
            }

            // 2. Process Maps
            foreach (var map in result._Maps) {
                collections.Maps.Add(new MapDto {
                    Path = map.AssetPath,
                    Hash = map.AssetHash,
                    GameMode = map.GameMode,
                    ObjectClass = "World"
                });
            }

            // 3. Process Replacements
            foreach (var repl in result._AssetReplacements) {
                collections.Replacements.Add(new ReplacementDto {
                    Path = repl.AssetPath,
                    Hash = repl.AssetHash,
                    ObjectClass = repl.ClassType
                });
            }

            // 4. Process Arbitrary
            foreach (var arb in result.ArbitraryAssets) {
                collections.Arbitrary.Add(new ArbitraryDto {
                    Path = arb.AssetPath,
                    Hash = arb.AssetHash,
                    ModName = arb.ModName,
                    ObjectClass = arb.ObjectName // Fallback as discussed
                });
            }

            return collections;
        }
    }

}