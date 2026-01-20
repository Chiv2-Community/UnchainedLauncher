using System.Text.Json.Serialization;
using System.Collections.Concurrent;
using UnchainedLauncher.UnrealModScanner.AssetSources;

namespace UnchainedLauncher.UnrealModScanner.Assets {
    /// <summary>
    /// Represents a generic Marker with nested GenericAssetEntry children
    /// </summary>
    public sealed class GenericMarkerEntry : GenericAssetEntryBase<GenericMarkerEntry> {
        // Parameterless constructor for 'new()' constraint
        public GenericMarkerEntry() { }

        /// <summary>
        /// Class Name of Marker children container (TMap)
        /// </summary>
        [JsonPropertyName("children_class_path")]
        public string ChildrenClassPath { get; private set; } = string.Empty;

        /// <summary>
        /// Contains Markers discovered by ReferenceDiscoveryProcessor
        /// </summary>
        [JsonPropertyName("children")]
        public ConcurrentBag<GenericAssetEntry> Children { get; private set; } = new();

        public void AddGenericEntry(GenericAssetEntry entry) => Children.Add(entry);

        /// <summary>
        /// Factory for leaf type including children + leaf-specific fields
        /// </summary>
        public static GenericMarkerEntry FromSource(
            IAssetSource source,
            string childrenClassPath,
            ConcurrentBag<GenericAssetEntry>? children = null,
            Dictionary<string, object?>? properties = null) {
            var asset = GenericAssetEntryBase<GenericMarkerEntry>.FromSource(source, properties);

            asset.ChildrenClassPath = childrenClassPath;
            asset.Children = children ?? new ConcurrentBag<GenericAssetEntry>();

            return asset;
        }
    }
}