using Newtonsoft.Json;
using System.Collections.Generic;

namespace C2GUILauncher.JsonModels {
    public record InstallEntry(
        [property: JsonProperty("InstallLocation")] string InstallLocation,
        [property: JsonProperty("NamespaceId")] string NamespaceId,
        [property: JsonProperty("ItemId")] string ItemId,
        [property: JsonProperty("ArtifactId")] string ArtifactId,
        [property: JsonProperty("AppVersion")] string AppVersion,
        [property: JsonProperty("AppName")] string AppName
        );

    public record EGSInstallList(
        [property: JsonProperty("InstallationList")] List<InstallEntry> InstallationList
    );
    //internal class EGSInstallList
    //{
    //}
}
