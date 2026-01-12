
using CUE4Parse.UE4.AssetRegistry.Objects;

namespace UnchainedLauncher.UnrealModScanner.Models {
    public class GameInternalAssetInfo {
        public string AssetPath { get; set; }
        public string ClassName { get; set; }
        public string FullPackageName { get; set; }

        public FAssetData AssetData { get; set; }
    }
}
