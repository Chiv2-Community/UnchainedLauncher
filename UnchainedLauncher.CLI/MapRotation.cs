using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;
using StructuredINI;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace UnchainedLauncher.CLI;

public sealed class MapProfile {
    public string? MapName { get; set; }
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

internal sealed record MapRotation(IReadOnlyList<string> Maps, int StartIndex) {
    internal static void ValidateMap(MapProfile? profile, string location) {
        if (profile?.MapName is not { } name || !Regex.IsMatch(name, @"\A/?[A-Za-z0-9_-]+(?:/[A-Za-z0-9_-]+)*(?:\.[A-Za-z0-9_-]+)?\z"))
            throw new ValidationException($"{location} requires a map-name containing a short map name or package path, without travel options.");
    }

    internal static MapRotation? Resolve(List<MapRotationEntry>? entries, Dictionary<string, MapProfile?>? profiles, string? startingMap) {
        if (entries is null) return null;
        if (entries.Count == 0) throw new ValidationException("map-rotation must contain at least one map.");
        var maps = new List<string>();
        foreach (var entry in entries) {
            var profile = entry?.Inline;
            if (entry?.Profile is { } name) {
                if (profiles is null || !profiles.TryGetValue(name, out profile))
                    throw new ValidationException($"Unknown map-profile '{name}'; define it under root map-profiles or use an inline map-name object.");
            }
            ValidateMap(profile, "map-rotation entry");
            maps.Add(profile!.MapName!);
        }
        var index = startingMap is null ? 0 : maps.FindIndex(map => map.Equals(startingMap, StringComparison.OrdinalIgnoreCase));
        if (index < 0) throw new ValidationException("starting-map must match a map-name in map-rotation.");
        return new MapRotation(maps.ToArray(), index);
    }

    internal void Apply(string directory) {
        var writer = new StructuredINIWriter();
        var values = new[] { KeyValuePair.Create("!MapList", "ClearArray") }
            .Concat(Maps.Select(map => KeyValuePair.Create(".MapList", map)))
            .Append(KeyValuePair.Create("MapListIndex", StartIndex.ToString(CultureInfo.InvariantCulture)));
        writer.BufferSection("/Script/TBL.TBLGameMode", values);
        if (!writer.WriteOut(Path.Combine(directory, "Game.ini")))
            throw new IOException("Could not apply map-rotation to the server profile; refusing to launch.");
    }
}