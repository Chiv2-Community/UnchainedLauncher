
using Newtonsoft.Json;
using UnchainedLauncher.UnrealModScanner.Models.Dto;

namespace UnchainedLauncher.UnrealModScanner.Models {
    public sealed class GameMapInfo : BaseAsset {
        /// <summary>
        /// GameMode Class Name (Short) for this map. Usually None for sublevels
        /// </summary>
        [JsonProperty("game_mode")]
        public string? GameMode { get; init; }
    }
}