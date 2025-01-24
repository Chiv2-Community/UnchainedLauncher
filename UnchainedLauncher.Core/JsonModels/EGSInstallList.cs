using System.Text.Json.Serialization;

namespace UnchainedLauncher.Core.JsonModels {
#pragma warning disable CA1507 // Disable "use nameof to express symbol names". Json structures need to be stable and renaming properties is a breaking change.

    public record InstallEntry(
        [property: JsonPropertyName("InstallLocation")] string InstallLocation,
        [property: JsonPropertyName("NamespaceId")] string NamespaceId,
        [property: JsonPropertyName("ItemId")] string ItemId,
        [property: JsonPropertyName("ArtifactId")] string ArtifactId,
        [property: JsonPropertyName("AppVersion")] string AppVersion,
        [property: JsonPropertyName("AppName")] string AppName
    );

    public record EGSInstallList(
        [property: JsonPropertyName("InstallationList")] List<InstallEntry> InstallationList
    );

#pragma warning restore CA1507

}