

using Newtonsoft.Json;

namespace UnchainedLauncher.UnrealModScanner.Models.Dto {
    /// <summary>
    /// Represents a generic result for any CDO property extraction.
    /// </summary>
    public class GenericAssetEntry {
        [JsonProperty("asset_path")]
        public string AssetPath { get; set; } = string.Empty;
        [JsonProperty("class_name")]
        public string ClassName { get; set; } = string.Empty;
        [JsonProperty("properties")]
        public Dictionary<string, object?> Properties { get; set; } = new();
    }
}
