using System.Text.Json.Serialization;
using UnchainedLauncher.UnrealModScanner.AssetSources;

namespace UnchainedLauncher.UnrealModScanner.Assets {
    public class GameMapInfo : BaseAsset {
        private GameMapInfo() { }

        public static GameMapInfo FromSource(
            IAssetSource source,
            string? gameMode,
            Dictionary<string, Dictionary<string, object?>>? settings) {
            var asset = new GameMapInfo();
            asset.Initialize(source);
            asset.GameMode = gameMode;
            asset.Settings = settings;
            return asset;
        }

        /// <summary>
        /// GameMode Class Name (Short) for this map. Usually None for sublevels
        /// </summary>
        [JsonPropertyName("game_mode")]
        public string? GameMode { get; private set; }

        [JsonPropertyName("settings")]
        public Dictionary<string, Dictionary<string, object?>>? Settings { get; set; }
    }
}