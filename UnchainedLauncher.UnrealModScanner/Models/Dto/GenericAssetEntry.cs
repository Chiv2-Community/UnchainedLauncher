

using Newtonsoft.Json;

namespace UnchainedLauncher.UnrealModScanner.Models.Dto {
    /// <summary>
    /// Represents a generic result for any CDO property extraction.
    /// </summary>
    public class GenericAssetEntry : BaseAsset {
        [JsonProperty("properties")]
        public Dictionary<string, object?> Properties { get; set; } = new();
    }
}
