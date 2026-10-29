using StructuredINI;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace UnchainedLauncher.CLI;

public sealed class MapProfile {
    public string? Name { get; set; }
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

internal sealed record MapRotation(IReadOnlyList<string> Maps, int StartIndex, IReadOnlyList<IniOverrides> Overrides, IReadOnlyList<string> Names) {
    internal bool HasOverrides => Overrides.Any(ini => !ini.IsEmpty);

    internal static IniOverrides ValidateMap(MapProfile? profile, string location) {
        if (profile?.MapName is not { } name || !Regex.IsMatch(name, @"\A/?[A-Za-z0-9_-]+(?:/[A-Za-z0-9_-]+)*(?:\.[A-Za-z0-9_-]+)?\z"))
            throw new ValidationException($"{location} requires a map-name containing a short map name or package path, without travel options.");
        if (profile.Name is { } displayName) ValidateName(displayName);
        var ini = IniOverrides.Parse(profile.Ini);
        if (ini.RawArguments.Any()) throw new ValidationException($"{location}.ini cannot use raw; per-map values must be written to INI files using typed overrides.");
        if (ini.Contains("Game", "/Script/TBL.TBLGameMode", "MapListIndex"))
            throw new ValidationException($"{location}.ini cannot change MapListIndex; use starting-map and map-rotation.");
        return ini;
    }

    private static string FriendlyName(string name, string mapName) {
        static string[] Words(string value) => Regex.Replace(value.Replace('-', ' ').Replace('_', ' '),
            @"(?<=[a-z0-9])(?=[A-Z])", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var mapWords = Words(mapName.Split('/').Last().Split('.')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(word => word, word => char.ToUpperInvariant(word[0]) + word[1..], StringComparer.OrdinalIgnoreCase);
        var words = Words(name).Select(word => word.ToUpperInvariant() switch {
            "FFA" => "FFA",
            "TDM" => "TDM",
            "TO" => "TO",
            "LTS" => "LTS",
            "LMS" => "LMS",
            _ => mapWords.GetValueOrDefault(word, word.ToLowerInvariant())
        });
        var label = string.Join(" ", words);
        return label.Length == 0 ? label : char.ToUpperInvariant(label[0]) + label[1..];
    }

    private static void ValidateName(string name) {
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name.EndsWith('.') ||
            name.Any(character => char.IsControl(character) || "<>:\"/\\|?*".Contains(character)) ||
            Regex.IsMatch(name.Split('.')[0], @"\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new ValidationException("Map profile name must be nonblank and a portable folder name, without surrounding whitespace, trailing dots, or reserved device names.");
    }

    internal static MapRotation? Resolve(List<MapRotationEntry>? entries, Dictionary<string, MapProfile?>? profiles, string? startingMap) {
        if (entries is null) return null;
        if (entries.Count == 0) throw new ValidationException("map-rotation must contain at least one map.");
        var maps = new List<string>();
        var overrides = new List<IniOverrides>();
        var names = new List<string>();
        var uniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries) {
            var profile = entry?.Inline;
            if (entry?.Profile is { } name) {
                if (profiles is null || !profiles.TryGetValue(name, out profile))
                    throw new ValidationException($"Unknown map-profile '{name}'; define it under root map-profiles or use an inline map-name object.");
            }
            var ini = ValidateMap(profile, "map-rotation entry");
            var displayName = profile!.Name ?? FriendlyName(entry!.Profile ?? profile.MapName!.Split('/').Last().Split('.')[0], profile.MapName!);
            ValidateName(displayName);
            if (!uniqueNames.Add(displayName)) throw new ValidationException($"Map rotation names must be unique within each server (case-insensitive); duplicate name '{displayName}'. Set a distinct name on each entry/profile.");
            names.Add(displayName);
            maps.Add(profile!.MapName!);
            overrides.Add(ini);
        }
        var index = startingMap is null ? 0 : maps.FindIndex(map => map.Equals(startingMap, StringComparison.OrdinalIgnoreCase));
        if (index < 0) throw new ValidationException("starting-map must match a map-name in map-rotation.");
        return new MapRotation(maps.ToArray(), index, overrides.ToArray(), names.ToArray());
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
