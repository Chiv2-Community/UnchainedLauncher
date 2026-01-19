using System.Collections.Concurrent;
using UnchainedLauncher.UnrealModScanner.Assets;
using UnchainedLauncher.UnrealModScanner.JsonModels;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.Services {
    public static class ModManifestConverter {
        public static ModManifest ProcessModScan(ModScanResult scanResult) {
            var manifest = new ModManifest {
                SchemaVersion = 1,
                ScannerVersion = "3.3.1",
                GeneratedAt = DateTime.UtcNow.ToString("u"),
                Paks = new List<PakInventoryDto>()
            };

            foreach (var entry in scanResult.Paks) {
                var pakInventory = new PakInventoryDto {
                    PakName = entry.Key,
                    PakPath = entry.Value.PakPath,
                    PakHash = entry.Value.PakHash,
                    Inventory = MapAssets(entry.Value)
                };

                manifest.Paks.Add(pakInventory);
            }

            return manifest;
        }

        private static AssetCollections MapAssets(PakScanResult result) {
            var collections = new AssetCollections();

            var KnownMarkerChildClassPaths = new ConcurrentBag<string>();

            // 1. Process Generic Markers
            // Structure: ConcurrentDictionary<string, ConcurrentDictionary<string, GenericMarkerEntry>>
            foreach (var outerKvp in result.GenericMarkers) {
                foreach (var innerKvp in outerKvp.Value) {
                    GenericMarkerEntry marker = innerKvp.Value;

                    KnownMarkerChildClassPaths.Add(marker.ChildrenClassPath);
                    var markerDto = new ModMarkerDto {
                        Path = marker.AssetPath,
                        Hash = marker.AssetHash,
                        ClassPath = marker.ClassPath,
                        ObjectClass = marker.ClassName,
                        // Map children paths to the DTO
                        AssociatedBlueprints = marker.Children.Select(c => c.AssetPath).ToList()
                    };

                    collections.Markers.Add(markerDto);

                    // Process the children (Blueprints) into the main collection
                    foreach (var childAsset in marker.Children) {
                        if (!collections.Blueprints.Any(x => x.Path == childAsset.AssetPath)) {
                            collections.Blueprints.Add(MapToBlueprintDto(childAsset));
                        }
                    }
                }
            }

            // Process orphaned blueprints
            // This requires the ModBase to be parsed by GenericCdoProcessor
            foreach (var (classPath, entries) in result.GenericEntries)
                if (classPath == "/Game/Mods/ArgonSDK/Mods/ArgonSDKModBase.ArgonSDKModBase_C")
                    foreach (var genericBlueprint in entries) {
                        var blueprint = MapToBlueprintDto(genericBlueprint);
                        blueprint.IsHidden = result.GenericMarkers.Any(x => x.Key == "DA_ModMarker_C");
                        collections.Blueprints.Add(blueprint);
                    }
                else {
                    Console.WriteLine($"Skipping {classPath}");
                }


            // 2. Process Maps
            foreach (var map in result._Maps) {
                collections.Maps.Add(new MapDto {
                    Path = map.AssetPath,
                    Hash = map.AssetHash,
                    ClassPath = map.ClassPath,
                    ObjectClass = map.ClassName,
                    GameMode = map.GameMode,
                    MapName = GetSetting(map.Settings, "MapName"),
                    MapDescription = GetSetting(map.Settings, "Description"),
                    AttackingFaction = GetSetting(map.Settings, "AttackingFaction"),
                    DefendingFaction = GetSetting(map.Settings, "DefendingFaction"),
                    GamemodeType = GetSetting(map.Settings, "GamemodeType"),
                    TBLDefaultGameMode = GetSetting(map.Settings, "TBLDefaultGameMode")
                });
            }

            // 3. Process Replacements
            foreach (var repl in result._AssetReplacements) {
                collections.Replacements.Add(new ReplacementDto {
                    Path = repl.AssetPath,
                    Hash = repl.AssetHash,
                    ClassPath = repl.ClassPath,
                    ObjectClass = repl.ClassName
                });
            }

            // 4. Process Arbitrary
            foreach (var arb in result.ArbitraryAssets) {
                collections.Arbitrary.Add(new ArbitraryDto {
                    Path = arb.AssetPath,
                    Hash = arb.AssetHash,
                    ClassPath = arb.ClassPath,
                    ObjectClass = arb.ClassName
                });
            }

            return collections;
        }

        private static BlueprintDto MapToBlueprintDto(GenericAssetEntry bp) {
            return new BlueprintDto {
                Path = bp.AssetPath,
                Hash = bp.AssetHash,
                ClassPath = bp.ClassPath,
                ObjectClass = bp.ClassName,

                ModName = GetProp<string>(bp.Properties, "ModName") ?? "",
                Version = GetProp<string>(bp.Properties, "ModVersion") ?? "1.0.0",
                ModDescription = GetProp<string>(bp.Properties, "ModDescription") ?? "",
                ModRepoURL = GetProp<string>(bp.Properties, "ModRepoURL") ?? "",
                Author = GetProp<string>(bp.Properties, "Author") ?? "",
                bEnableByDefault = GetProp<bool>(bp.Properties, "bEnableByDefault"),
                bSilentLoad = GetProp<bool>(bp.Properties, "bSilentLoad"),
                bShowInGUI = GetProp<bool>(bp.Properties, "bShowInGUI"),
                bClientside = GetProp<bool>(bp.Properties, "bClientside"),
                bOnlineOnly = GetProp<bool>(bp.Properties, "bOnlineOnly"),
                bHostOnly = GetProp<bool>(bp.Properties, "bHostOnly"),
                bAllowOnFrontend = GetProp<bool>(bp.Properties, "bAllowOnFrontend")
            };
        }

        private static string? GetSetting(Dictionary<string, Dictionary<string, object?>>? settings, string key) {
            if (settings == null) return null;
            foreach (var category in settings.Values) {
                if (category.TryGetValue(key, out var value))
                    return value?.ToString();
            }
            return null;
        }

        private static T? GetProp<T>(Dictionary<string, object?> props, string key) {
            if (props.TryGetValue(key, out var value)) {
                // Handle cases where numbers might be boxed as different types
                if (value is T typedValue) return typedValue;

                try { return (T)Convert.ChangeType(value, typeof(T)); }
                catch { return default; }
            }
            return default;
        }
    }
}