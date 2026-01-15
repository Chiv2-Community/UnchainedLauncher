using Newtonsoft.Json;
using System.Text.Json;

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
            var json = JsonConvert.SerializeObject(options, new JsonSerializerSettings {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Include
            });
            File.WriteAllText(path, json);
        }

        private static readonly JsonSerializerOptions _options = new() { WriteIndented = true };

        public static void GenerateDefaultConfig(string outputPath) {
            var defaultConfig = new ScanOptions();
            var json = JsonConvert.SerializeObject(defaultConfig, new JsonSerializerSettings {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Include,
            });
            File.WriteAllText(outputPath, json);
        }
    }

}