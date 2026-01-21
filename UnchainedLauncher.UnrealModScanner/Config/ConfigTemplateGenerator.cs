using Newtonsoft.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnchainedLauncher.UnrealModScanner.Config.Games;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace UnchainedLauncher.UnrealModScanner.Config {
    
    public static class ConfigTemplateGenerator {
        public static readonly JsonSerializerOptions SerializerSettings = new JsonSerializerOptions {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        
        public static void GenerateDefault(string path) {
            //var options = new ScanOptions {
            //    CdoProcessors = new List<CdoProcessorConfig> {
            //        new() {
            //            TargetClassName = "DA_Items_C",
            //            Properties = new List<PropertyConfig> {
            //                new() { Name = "ItemName", Mode = EExtractionMode.String },
            //                new() { Name = "BaseStats", Mode = EExtractionMode.Json }
            //            }
            //        }
            //    },
            //    MarkerProcessors = new List<MarkerDiscoveryConfig> {
            //        new() {
            //            MarkerClassName = "DA_ModMarker_C", //
            //            MapPropertyName = "ModActors",      //
            //            ReferencedBlueprintProperties = new List<PropertyConfig> {
            //                new() { Name = "ModName", Mode = EExtractionMode.String }
            //            }
            //        }
            //    }
            //};

            var options = GameScanOptions.Chivalry2;
            var json = JsonSerializer.Serialize(options, SerializerSettings);
            File.WriteAllText(path, json);
        }
        
        public static void GenerateDefaultConfig(string outputPath) {
            var defaultConfig = GameScanOptions.Chivalry2;
            var json = JsonSerializer.Serialize(defaultConfig, SerializerSettings);
            File.WriteAllText(outputPath, json);
        }
    }

}