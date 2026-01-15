using CUE4Parse.UE4.Versions;

namespace UnchainedLauncher.UnrealModScanner.Config.Games {
    public static class GameScanOptions
{
    public static ScanOptions Chivalry2 => new()
    {
        GameVersion = EGame.GAME_UE4_25,
        AesKey = "0x0000000000000000000000000000000000000000000000000000000000000000",
        IsWhitelist = false,
        PathFilters = [
            "Abilities","AI","Animation","Audio","Blueprint","Characters","Cinematics",
            "Collections","Config","Custom_Lens_Flare_VFX","Customization","Debug",
            "Developers","Environments","FX","Game","GameModes","Gameplay","Interactables",
            "Inventory","Localization","MapGen","Maps","MapsTest","Materials","Meshes",
            "Trailer_Cinematic","UI","Weapons","Engine","Mannequin"
        ],
        CdoProcessors = [
            new() { 
                TargetClassName = "TBL/Content/Mods/ArgonSDK/Mods/ArgonSDKModBase", 
                Properties = [
                    new() { Name = "ModName", Mode = EExtractionMode.String },
                    new() { Name = "ModVersion", Mode = EExtractionMode.String },
                    new() { Name = "ModDescription", Mode = EExtractionMode.String },
                    new() { Name = "ModRepoURL", Mode = EExtractionMode.String },
                    new() { Name = "Author", Mode = EExtractionMode.String },
                    new() { Name = "bEnableByDefault", Mode = EExtractionMode.Raw },
                    new() { Name = "bSilentLoad", Mode = EExtractionMode.Raw },
                    new() { Name = "bShowInGUI ", Mode = EExtractionMode.Raw },
                    new() { Name = "bClientside", Mode = EExtractionMode.Raw },
                    new() { Name = "bOnlineOnly", Mode = EExtractionMode.Raw },
                    new() { Name = "bHostOnly", Mode = EExtractionMode.Raw },
                    new() { Name = "bAllowOnFrontend", Mode = EExtractionMode.Raw },
                ]
            },
            new() { 
                TargetClassName = "/Game/Mods/ArgonSDK/Mods/ArgonSDKModBase", 
                Properties = [
                    new() { Name = "ModName", Mode = EExtractionMode.String },
                    new() { Name = "ModVersion", Mode = EExtractionMode.String },
                    new() { Name = "ModDescription", Mode = EExtractionMode.String },
                    new() { Name = "ModRepoURL", Mode = EExtractionMode.String },
                    new() { Name = "Author", Mode = EExtractionMode.String },
                    new() { Name = "bEnableByDefault", Mode = EExtractionMode.Raw },
                    new() { Name = "bSilentLoad", Mode = EExtractionMode.Raw },
                    new() { Name = "bShowInGUI ", Mode = EExtractionMode.Raw },
                    new() { Name = "bClientside", Mode = EExtractionMode.Raw },
                    new() { Name = "bOnlineOnly", Mode = EExtractionMode.Raw },
                    new() { Name = "bHostOnly", Mode = EExtractionMode.Raw },
                    new() { Name = "bAllowOnFrontend", Mode = EExtractionMode.Raw },
                ]
            },
            new() { 
                TargetClassName = "TBL/Content/Mods/ArgonSDK/Mods/ModLoading/DA_MapInfo", 
                Properties = [
                    new() { Name = "DisplayName", Mode = EExtractionMode.String },
                    new() { Name = "MapWidgetLocation", Mode = EExtractionMode.Raw },
                    new() { Name = "VariantDescriptions", Mode = EExtractionMode.Raw },
                    new() { Name = "VariantImages", Mode = EExtractionMode.Raw },
                    new() { Name = "VariantDescriptions", Mode = EExtractionMode.Raw },
                ]
            }
        ],
        MarkerProcessors = [
            new() { 
                MarkerClassName = "DA_ModMarker_C", 
                MapPropertyName = "ModActors", 
                ReferencedBlueprintProperties = [
                    new() { Name = "ModName", Mode = 0 },
                    new() { Name = "ModVersion", Mode = 0 },
                    new() { Name = "ModDescription", Mode = 0 },
                    new() { Name = "ModRepoURL", Mode = 0 },
                    new() { Name = "Author", Mode = 0 },
                    new() { Name = "bEnableByDefault", Mode = (EExtractionMode)2 },
                    new() { Name = "bSilentLoad", Mode = (EExtractionMode)2 },
                    new() { Name = "bShowInGUI", Mode = (EExtractionMode)2 },
                    new() { Name = "bClientside", Mode = (EExtractionMode)2 },
                    new() { Name = "bOnlineOnly", Mode = (EExtractionMode)2 },
                    new() { Name = "bHostOnly", Mode = (EExtractionMode)2 },
                    new() { Name = "bAllowOnFrontend", Mode = (EExtractionMode)2 }
                ]
            }
        ]
    };
}
}