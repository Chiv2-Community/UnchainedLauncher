
using Newtonsoft.Json;
using UnchainedLauncher.UnrealModScanner.Assets;

namespace UnchainedLauncher.UnrealModScanner.Models.Chivalry2 {
    /// <summary>
    /// Chivalry 2 Mod markers. ModActors TMap contains Object Path+Description
    /// </summary>
    [Obsolete("Use GenericMarkerEntry in the future")]
    public sealed class ModMarkerInfo : BaseAsset {

        [JsonProperty("blueprints", Required = Required.DisallowNull)]
        public List<BlueprintModInfo> Blueprints { get; private set; } = new();

        // TODO
        //public required GameMapInfo Map { get; init;  }
    }
}
