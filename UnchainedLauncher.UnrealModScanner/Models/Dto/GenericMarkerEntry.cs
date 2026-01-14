using Newtonsoft.Json;
using System.Collections.Concurrent;

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
        public ConcurrentBag<GenericAssetEntry> Children { get; set; } = new();
        public void AddGenericEntry(GenericAssetEntry entry) => Children.Add(entry);
    }
}