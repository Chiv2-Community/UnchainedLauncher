using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.UnrealModScanner.JsonModels;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab {
    // Keep unresolved legacy entries in their original format until their exact release is available.
    public class ServerBlueprintSelections : ObservableCollection<BlueprintDto> {
        internal List<JsonElement> LegacySelections { get; } = [];

        public void ResolveLegacySelections(IModManager modManager) {
            foreach (var entry in LegacySelections.ToList()) {
                var coordinates = JsonHelpers.Deserialize<ReleaseCoordinates>(entry.GetRawText()).Result;
                if (coordinates == null) continue;
                var blueprints = modManager.GetRelease(coordinates).ToList()
                    .SelectMany(release => release.Manifest.RelevantBlueprints()).Distinct().ToList();
                if (blueprints.Count == 0) continue;
                LegacySelections.Remove(entry);
                foreach (var blueprint in blueprints.Where(blueprint => !Contains(blueprint)))
                    Add(blueprint);
            }
        }
    }

    public class ServerBlueprintSelectionsConverter : JsonConverter<ObservableCollection<BlueprintDto>> {
        public override ObservableCollection<BlueprintDto> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
            using var document = JsonDocument.ParseValue(ref reader);
            var selections = new ServerBlueprintSelections();
            foreach (var entry in document.RootElement.EnumerateArray()) {
                if (entry.TryGetProperty("Org", out _) && entry.TryGetProperty("ModuleName", out _))
                    selections.LegacySelections.Add(entry.Clone());
                else
                    selections.Add(entry.Deserialize<BlueprintDto>(options)!);
            }
            return selections;
        }

        public override void Write(Utf8JsonWriter writer, ObservableCollection<BlueprintDto> value, JsonSerializerOptions options) {
            writer.WriteStartArray();
            foreach (var blueprint in value)
                JsonSerializer.Serialize(writer, blueprint, options);
            if (value is ServerBlueprintSelections selections)
                foreach (var entry in selections.LegacySelections)
                    entry.WriteTo(writer);
            writer.WriteEndArray();
        }
    }
}
