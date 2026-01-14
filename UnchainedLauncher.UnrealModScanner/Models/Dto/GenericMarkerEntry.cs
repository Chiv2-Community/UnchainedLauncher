using Newtonsoft.Json;

namespace UnchainedLauncher.UnrealModScanner.Models.Dto {
    /// <summary>
    /// Represents a generic MArker with nested GenericAssetEntry children
    /// </summary>
    public class GenericMarkerEntry : GenericAssetEntry {
        /// <summary>
        /// Class Name of Marker children container (TMap)
        /// </summary>
        [JsonProperty("children_class_name")]
        public string ChildrenClassName { get; set; } = string.Empty;
        /// <summary>
        /// Contains Markers discovered by ReferenceDiscoveryProcessor
        /// </summary>
        [JsonProperty("children")]
        public string Children { get; set; } = string.Empty;
    }
}