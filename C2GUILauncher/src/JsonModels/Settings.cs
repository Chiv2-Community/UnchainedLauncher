using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace C2GUILauncher.JsonModels
{
    record SavedSettings(
        [property: JsonProperty("installation_type")] InstallationType InstallationType,
        [property: JsonProperty("enable_plugin_logging")] bool EnablePluginLogging,
        [property: JsonProperty("enable_plugin_automatic_updates")] bool EnablePluginAutomaticUpdates
    );

    [JsonConverter(typeof(StringEnumConverter))]
    public enum InstallationType { NotSet, Steam, EpicGamesStore }
    
}
