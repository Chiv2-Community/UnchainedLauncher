using System.Text.Json.Serialization;

namespace UnchainedLauncher.UnrealModScanner.Models.Dto {
    public class FailedPackage {
        [JsonPropertyName("package_path")]
        [JsonPropertyOrder(-2)]
        public string PackagePath { get; set; } = string.Empty;
    }
}