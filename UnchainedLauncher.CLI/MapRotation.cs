using StructuredINI;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace UnchainedLauncher.CLI;

public sealed class MapProfile {
    public string? MapName { get; set; }
    public Dictionary<string, Dictionary<string, Dictionary<string, object?>>>? Ini { get; set; }
}

public sealed record MapRotationEntry(string? Profile, MapProfile? Inline);

internal sealed class MapRotationEntryYamlConverter : IYamlTypeConverter {
    public bool Accepts(Type type) => type == typeof(MapRotationEntry);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer) {
        if (parser.TryConsume<Scalar>(out var scalar)) {
            if (string.IsNullOrWhiteSpace(scalar.Value) || scalar.Style == ScalarStyle.Plain &&
                (scalar.Value == "~" || scalar.Value.Equals("null", StringComparison.OrdinalIgnoreCase)))
                throw new YamlException("map-rotation entries must be profile names or map-name objects.");
            return new MapRotationEntry(scalar.Value, null);
        }
        if (!parser.Accept<MappingStart>(out _))
            throw new YamlException("map-rotation entries must be profile names or map-name objects.");
        return new MapRotationEntry(null, (MapProfile?)rootDeserializer(typeof(MapProfile)));
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer) =>
        throw new NotSupportedException("Map rotation YAML is read-only.");
}

internal sealed record MapRotation(IReadOnlyList<string> Maps, int StartIndex, IReadOnlyList<IniOverrides> Overrides) {
    internal bool HasOverrides => Overrides.Any(ini => !ini.IsEmpty);

    internal static IniOverrides ValidateMap(MapProfile? profile, string location) {
        if (profile?.MapName is not { } name || !Regex.IsMatch(name, @"\A/?[A-Za-z0-9_-]+(?:/[A-Za-z0-9_-]+)*(?:\.[A-Za-z0-9_-]+)?\z"))
            throw new ValidationException($"{location} requires a map-name containing a short map name or package path, without travel options.");
        var ini = IniOverrides.Parse(profile.Ini);
        if (ini.RawArguments.Any()) throw new ValidationException($"{location}.ini cannot use raw; per-map values must be written to INI files using typed overrides.");
        if (ini.Contains("Game", "/Script/TBL.TBLGameMode", "MapListIndex"))
            throw new ValidationException($"{location}.ini cannot change MapListIndex; use starting-map and map-rotation.");
        return ini;
    }

    internal static MapRotation? Resolve(List<MapRotationEntry>? entries, Dictionary<string, MapProfile?>? profiles, string? startingMap) {
        if (entries is null) return null;
        if (entries.Count == 0) throw new ValidationException("map-rotation must contain at least one map.");
        var maps = new List<string>();
        var overrides = new List<IniOverrides>();
        foreach (var entry in entries) {
            var profile = entry?.Inline;
            if (entry?.Profile is { } name) {
                if (profiles is null || !profiles.TryGetValue(name, out profile))
                    throw new ValidationException($"Unknown map-profile '{name}'; define it under root map-profiles or use an inline map-name object.");
            }
            var ini = ValidateMap(profile, "map-rotation entry");
            maps.Add(profile!.MapName!);
            overrides.Add(ini);
        }
        var index = startingMap is null ? 0 : maps.FindIndex(map => map.Equals(startingMap, StringComparison.OrdinalIgnoreCase));
        if (index < 0) throw new ValidationException("starting-map must match a map-name in map-rotation.");
        return new MapRotation(maps.ToArray(), index, overrides.ToArray());
    }

    internal void Apply(string directory) {
        var writer = new StructuredINIWriter();
        BufferInto(writer);
        IniPatches.Write(directory, new(StringComparer.OrdinalIgnoreCase) { ["Game"] = writer });
    }

    internal void BufferInto(StructuredINIWriter writer) {
        var values = IniPatches.Array("MapList", Maps)
            .Append(KeyValuePair.Create("MapListIndex", StartIndex.ToString(CultureInfo.InvariantCulture)));
        writer.MergeSection("/Script/TBL.TBLGameMode", values);
    }
}
