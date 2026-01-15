using Newtonsoft.Json;

namespace UnchainedLauncher.UnrealModScanner.Models.Dto {
    public class FailedPackage {
        [JsonProperty("package_path", Order = -2)]
        public string PackagePath { get; set; } = string.Empty;
    }
}