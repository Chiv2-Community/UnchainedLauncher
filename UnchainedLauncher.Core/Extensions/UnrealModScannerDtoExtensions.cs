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
            // An explicit missing rules class identifies a support level, even if
            // it serialized a default/native GamemodeType. Prefixes are display metadata.
            if (HasRulesClass(map.GameMode) || HasRulesClass(map.TBLDefaultGameMode)) return true;
            if (IsExplicitlyAbsent(map.GameMode) || IsExplicitlyAbsent(map.TBLDefaultGameMode)) return false;
            // Older manifests may omit the class fields; retain their mode metadata fallback.
            return HasRulesClass(map.GamemodeType);
        }
    }

    private static bool HasRulesClass(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !IsExplicitlyAbsent(value);

    private static bool IsExplicitlyAbsent(string? value) =>
        value is not null && (string.IsNullOrWhiteSpace(value)
            || string.Equals(value.Trim(), "None", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value.Trim(), "null", StringComparison.OrdinalIgnoreCase));

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