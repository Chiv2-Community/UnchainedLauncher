
using UnchainedLauncher.Core.JsonModels.Metadata;
using UnchainedLauncher.UnrealModScanner.PakScanning;
using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.Services {
    public static class PakTechnicalManifestProcessor {
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
