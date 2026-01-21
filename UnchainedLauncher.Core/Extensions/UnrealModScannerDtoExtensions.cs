using UnchainedLauncher.UnrealModScanner.JsonModels;

namespace UnchainedLauncher.Core.Extensions;

public static class UnrealModScannerDtoExtensions {
    extension(MapDto map) {
        public string TravelToMapString() {
            var lastSlash = map.Path.LastIndexOf('/');
            var lastDot = map.Path.LastIndexOf('.');
            return map.Path[(lastSlash + 1)..lastDot];
        }
    }

    extension(AssetCollections manifest) {
        public IEnumerable<BlueprintDto> RelevantBlueprints() =>
            manifest.Blueprints.Where(x => (!x.IsHidden ?? false) && !x.bClientside);

        public IEnumerable<MapDto> RelevantMaps() =>
            manifest.Maps.Where(x => !string.IsNullOrEmpty(x.GamemodeType?.Trim()));
    }
}