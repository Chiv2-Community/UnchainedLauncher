using System.Text.Json.Serialization;
using UnchainedLauncher.UnrealModScanner.AssetSources;

namespace UnchainedLauncher.UnrealModScanner.Assets {
    /// <summary>
    /// Base class for all generic asset entries 
    /// </summary>
    public abstract class GenericAssetEntryBase<TDerived> : BaseAsset
        where TDerived : GenericAssetEntryBase<TDerived>, new() {
        [JsonPropertyName("properties")]
        public Dictionary<string, object?> Properties { get; protected set; } = new();

        // Required for 'new()' constraint
        protected GenericAssetEntryBase() { }

        /// <summary>
        /// Intermediate initialization for derived types
        /// </summary>
        protected void InitializeGeneric(IAssetSource source, Dictionary<string, object?>? properties) {
            Initialize(source);      // BaseAsset logic
            Properties = properties; // ?? new Dictionary<string, object?>();
        }

        /// <summary>
        /// Factory for any derived type
        /// </summary>
        public static TDerived FromSource(IAssetSource source, Dictionary<string, object?>? properties) {
            var asset = new TDerived();
            asset.InitializeGeneric(source, properties);
            return asset;
        }
    }
}