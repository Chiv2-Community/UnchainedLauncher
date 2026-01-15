using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnchainedLauncher.UnrealModScanner.AssetSources;

namespace UnchainedLauncher.UnrealModScanner.Assets
{
    /// <summary>
    /// Represents a generic Marker with nested GenericAssetEntry children
    /// </summary>
    public sealed class GenericMarkerEntry : GenericAssetEntryBase<GenericMarkerEntry>
    {
        // Parameterless constructor for 'new()' constraint
        public  GenericMarkerEntry() { }

        /// <summary>
        /// Class Name of Marker children container (TMap)
        /// </summary>
        [JsonProperty("children_class_name")]
        public string ChildrenClassName { get; private set; } = string.Empty;

        /// <summary>
        /// Contains Markers discovered by ReferenceDiscoveryProcessor
        /// </summary>
        [JsonProperty("children")]
        public ConcurrentBag<GenericAssetEntry> Children { get; private set; } = new();

        public void AddGenericEntry(GenericAssetEntry entry) => Children.Add(entry);

        /// <summary>
        /// Factory for leaf type including children + leaf-specific fields
        /// </summary>
        public static GenericMarkerEntry FromSource(
            IAssetSource source,
            string childrenClassName,
            ConcurrentBag<GenericAssetEntry>? children = null,
            Dictionary<string, object?>? properties = null)
        {
            var asset = GenericAssetEntryBase<GenericMarkerEntry>.FromSource(source, properties);

            asset.ChildrenClassName = childrenClassName;
            asset.Children = children ?? new ConcurrentBag<GenericAssetEntry>();

            return asset;
        }
    }
}