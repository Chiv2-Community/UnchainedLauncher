using UnchainedLauncher.UnrealModScanner.JsonModels;

namespace UnchainedLauncher.Core.Extensions;
    
public static class UnrealModScannerDtoExtensions {
    public static string TravelToMapString(this MapDto map) {
        var lastSlash = map.Path.LastIndexOf('/');
        var lastDot = map.Path.LastIndexOf('.');
        return map.Path[(lastSlash + 1)..lastDot]; 
    }
}