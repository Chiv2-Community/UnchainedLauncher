

using Newtonsoft.Json;
using UnchainedLauncher.UnrealModScanner.Models.Dto;

namespace UnchainedLauncher.UnrealModScanner.Models.Chivalry2 {
    /// <summary>
    /// Chivalry 2 Mod actor information retrieved from blueprints
    /// </summary>
    [Obsolete("Use GenericAssetEntry with properties or convert into this class to use in .Core")]
    public sealed class BlueprintModInfo : BaseAsset {
        /// <summary>
        /// Display name of the mod
        /// </summary>
        [JsonProperty("mod_name")]
        public required string ModName { get; init; }
        /// <summary>
        /// Semantic version of the actor
        /// </summary>
        [JsonProperty("mod_version")]
        public required string Version { get; init; }
        /// <summary>
        /// Author or comma-separated list of authors
        /// Used as Organization in metadata
        /// </summary>
        [JsonProperty("mod_author")]
        public required string Author { get; init; }
        /// <summary>
        /// If true, mod can be loaded on the client during online play
        /// </summary>
        [JsonProperty("is_clientside")]
        public required bool IsClientSide { get; init; }
    }
}