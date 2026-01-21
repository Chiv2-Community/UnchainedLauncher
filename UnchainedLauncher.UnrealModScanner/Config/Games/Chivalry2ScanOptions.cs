using CUE4Parse.UE4.Versions;

namespace UnchainedLauncher.UnrealModScanner.Config.Games {
    public static class GameScanOptions {
        public static ScanOptions Chivalry2 => new(
            VanillaPakNames: ["pakchunk0-WindowsNoEditor.pak"],
            GameVersion: EGame.GAME_UE4_25,
            AesKey: "0x0000000000000000000000000000000000000000000000000000000000000000",
            ScanFilter: new AssetsOnlyScanFilter().With(new Blacklist(["a", "b", "c"])), // scan everything that is an asset
            VanillaAssetPaths: new List<string>{
                "Abilities", "AI", "Animation", "Audio", "Blueprint", "Characters", "Cinematics",
                "Collections", "Config", "Custom_Lens_Flare_VFX", "Customization", "Debug",
                "Developers", "Environments", "FX", "Game", "GameModes", "Gameplay", "Interactables",
                "Inventory", "Localization", "MapGen", "Maps", "MapsTest", "Materials", "Meshes",
                "Trailer_Cinematic", "UI", "Weapons", "Engine", "Mannequin"
            }.Select(x => $"TBL/Content/{x}").ToList(),
            CdoProcessors: [
                new() {
                    TargetClassName = "/Game/Mods/ArgonSDK/Mods/ArgonSDKModBase",
                    Properties = [
                        new("ModName", EExtractionMode.String),
                        new("ModVersion", EExtractionMode.String),
                        new("ModDescription", EExtractionMode.String),
                        new("ModRepoURL", EExtractionMode.String),
                        new("Author", EExtractionMode.String),
                        new("bEnableByDefault", EExtractionMode.Raw),
                        new("bSilentLoad", EExtractionMode.Raw),
                        new("bShowInGUI ", EExtractionMode.Raw),
                        new("bClientside", EExtractionMode.Raw),
                        new("bOnlineOnly", EExtractionMode.Raw),
                        new("bHostOnly", EExtractionMode.Raw),
                        new("bAllowOnFrontend", EExtractionMode.Raw),
                    ]
                },
                new() {
                    TargetClassName = "TBL/Content/Mods/ArgonSDK/Mods/ModLoading/DA_MapInfo",
                    Properties = [
                        new("DisplayName", EExtractionMode.String),
                        new("MapWidgetLocation", EExtractionMode.Raw),
                        new("VariantDescriptions", EExtractionMode.Raw),
                        new("VariantImages", EExtractionMode.Raw),
                        new("VariantDescriptions", EExtractionMode.Raw),
                    ]
                }
            ],
            MarkerProcessors: [
                new() {
                    MarkerClassName = "DA_ModMarker_C",
                    MapPropertyName = "ModActors",
                    ReferencedBlueprintProperties = [
                        new("ModName", EExtractionMode.String),
                        new("ModVersion", EExtractionMode.String),
                        new("ModDescription", EExtractionMode.String),
                        new("ModRepoURL", EExtractionMode.String),
                        new("Author", EExtractionMode.String),
                        new("bEnableByDefault", EExtractionMode.Raw),
                        new("bSilentLoad", EExtractionMode.Raw),
                        new("bShowInGUI", EExtractionMode.Raw),
                        new("bClientside", EExtractionMode.Raw),
                        new("bOnlineOnly", EExtractionMode.Raw),
                        new("bHostOnly", EExtractionMode.Raw),
                        new("bAllowOnFrontend", EExtractionMode.Raw)
                    ]
                }
            ],
            Targets: null
        );
    }
}