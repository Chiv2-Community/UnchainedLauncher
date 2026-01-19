
using Newtonsoft.Json;
using UnchainedLauncher.UnrealModScanner.Assets;

namespace UnchainedLauncher.UnrealModScanner.Models.Chivalry2 {
    /// <summary>
    /// Chivalry 2 Mod markers. ModActors TMap contains Object Path+Description
    /// </summary>
    [Obsolete("Use GenericMarkerEntry in the future")]
    public sealed class ModMarkerInfo : BaseAsset {

        [JsonProperty("blueprints")]
        public List<BlueprintModInfo> Blueprints { get; } = new();

        // TODO
        //public required GameMapInfo Map { get; init;  }
    }
}