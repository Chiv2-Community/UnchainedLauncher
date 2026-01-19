using Newtonsoft.Json;
using UnchainedLauncher.UnrealModScanner.Config.Games;

namespace UnchainedLauncher.UnrealModScanner.Config {
    public static class ConfigTemplateGenerator {
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

            var options = new ScanOptions();
            var json = JsonConvert.SerializeObject(options, SerializerSettings);
            File.WriteAllText(path, json);
        }

        private static JsonSerializerSettings SerializerSettings = new JsonSerializerSettings {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Include,
            Converters = { new Newtonsoft.Json.Converters.StringEnumConverter() }
        };

        public static void GenerateDefaultConfig(string outputPath) {
            var defaultConfig = GameScanOptions.Chivalry2;
            var json = JsonConvert.SerializeObject(defaultConfig, SerializerSettings);
            File.WriteAllText(outputPath, json);
        }
    }

}