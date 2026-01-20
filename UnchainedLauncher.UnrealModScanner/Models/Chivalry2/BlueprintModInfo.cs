

using System.Text.Json.Serialization;
using UnchainedLauncher.UnrealModScanner.Assets;

namespace UnchainedLauncher.UnrealModScanner.Models.Chivalry2 {
    /// <summary>
    /// Chivalry 2 Mod actor information retrieved from blueprints
    /// </summary>
    [Obsolete("Use GenericAssetEntry with properties or convert into this class to use in .Core")]
    public sealed class BlueprintModInfo : BaseAsset {
        /// <summary>
        /// Display name of the mod
        /// </summary>
        [JsonPropertyName("mod_name")]
        public required string ModName { get; init; }
        /// <summary>
        /// Semantic version of the actor
        /// </summary>
        [JsonPropertyName("mod_version")]
        public required string Version { get; init; }
        /// <summary>
        /// Author or comma-separated list of authors
        /// Used as Organization in metadata
        /// </summary>
        [JsonPropertyName("mod_author")]
        public required string Author { get; init; }
        /// <summary>
        /// If true, mod can be loaded on the client during online play
        /// </summary>
        [JsonPropertyName("is_clientside")]
        public required bool IsClientSide { get; init; }
    }
}