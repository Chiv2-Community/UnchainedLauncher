

namespace UnchainedLauncher.UnrealModScanner.Models.Dto {
    /// <summary>
    /// Holds a reference found in a Marker to be processed later.
    /// </summary>
    public class PendingBlueprintReference {
        public string SourceMarkerPath { get; set; } = string.Empty;
        
        public string SourceMarkerClassName { get; set; } = string.Empty;
        public string TargetBlueprintPath { get; set; } = string.Empty;
        public string TargetClassName { get; set; } = string.Empty;
        public string SourcePakFile { get; set; } = string.Empty;
    }
}
