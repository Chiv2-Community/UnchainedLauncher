using System.Text.Json.Serialization;
using UnchainedLauncher.UnrealModScanner.Assets;
using UnchainedLauncher.UnrealModScanner.AssetSources;

namespace UnchainedLauncher.UnrealModScanner.Models.Chivalry2 {
    /// <summary>
    /// Generic Chivalry 2 asset with fallback for old mods
    /// </summary>
    [Obsolete("Old mods can be retrieved via GenericCDOProcessor")]
    public sealed class ArbitraryAssetInfo : BaseAsset {
        /// <summary>
        /// Fallback for old Chivalry 2 mod actors (no Mod marker provided)
        /// </summary>
        [JsonPropertyName("mod_name")]
        public string ModName { get; set; } = string.Empty;


        public static ArbitraryAssetInfo FromSource(
            IAssetSource source,
            string? modName) {
            var asset = new ArbitraryAssetInfo();
            asset.Initialize(source);
            asset.ModName = modName;
            return asset;
        }
    }


}