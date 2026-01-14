using Newtonsoft.Json;
using UnchainedLauncher.UnrealModScanner.Models.Dto;

namespace UnchainedLauncher.UnrealModScanner.Models.Chivalry2 {
    /// <summary>
    /// Generic Chivalry 2 asset with fallback for old mods
    /// </summary>
    [Obsolete("Old mods can be retrieved via GenericCDOProcessor")]
    public sealed class ArbitraryAssetInfo : BaseAsset {
        /// <summary>
        /// Fallback for old Chivalry 2 mod actors (no Mod marker provided)
        /// </summary>
        [JsonProperty("mod_name")]
        public string ModName { get; init; } = string.Empty;
    }
}