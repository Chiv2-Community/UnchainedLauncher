using UnchainedLauncher.UnrealModScanner.JsonModels;

namespace UnchainedLauncher.Core.Extensions;

public static class UnrealModScannerDtoExtensions {
    extension(MapDto map) {
        public string TravelToMapString() {
            var lastSlash = map.Path.LastIndexOf('/');
            var lastDot = map.Path.LastIndexOf('.');
            return map.Path[(lastSlash + 1)..lastDot];
        }

        public bool IsRelevant() {
            return !string.IsNullOrEmpty(map.GamemodeType?.Trim());
        }
    }

    extension(BlueprintDto blueprintDto) {
        public bool IsRelevant() {
            var shouldBeHidden = blueprintDto.IsHidden ?? false;
            var isClientSideOnly = blueprintDto.bClientside ?? false;
            var isShownOnFrontend = blueprintDto.bAllowOnFrontend ?? false;
            var hasShowInGUI = blueprintDto.bShowInGUI != null;

            return !shouldBeHidden && !isClientSideOnly && !isShownOnFrontend && hasShowInGUI;
        }
    }

    extension(AssetCollections manifest) {
        public IEnumerable<BlueprintDto> RelevantBlueprints() =>
            manifest.Blueprints.Where(x => x.IsRelevant());

        public IEnumerable<MapDto> RelevantMaps() =>
            manifest.Maps.Where(x => x.IsRelevant());
    }
}