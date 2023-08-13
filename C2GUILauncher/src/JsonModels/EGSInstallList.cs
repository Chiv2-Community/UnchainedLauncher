using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace C2GUILauncher.JsonModels
{
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
