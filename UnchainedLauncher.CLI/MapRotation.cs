using StructuredINI;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace UnchainedLauncher.CLI;

public sealed class MapProfile : SettingsBlock {
    public string? Preset { get; set; }
    public string? Map { get; set; }
    public string? Name { get; set; }
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

internal sealed record NativeIniLayer(string Group, string Name, IniOverrides Ini);

internal sealed record MapRotation(IReadOnlyList<string> Maps, int StartIndex, IReadOnlyList<IniOverrides> Overrides, IReadOnlyList<string> Names, IReadOnlyList<string> Profiles) {
    private const string Section = "/Script/TBL.TBLGameMode";
    [INISection(Section)]
    private sealed record RotationIni(string[] MapList, int MapListIndex);
    [INISection(Section)]
    private sealed record SavedRotationIni(int MapListIndex = -1);
    internal IReadOnlyList<IReadOnlyList<string>>? Actors { get; init; }
    internal IReadOnlyList<NativeIniLayer> Layers { get; init; } = [];
    internal bool HasOverrides => Layers.Any(layer => !layer.Ini.IsEmpty);

    internal static IniOverrides ValidateMap(MapProfile? profile, string location) {
        if (profile is { MapName: not null } && (profile.ModActors is not null || profile.Mods is not null || profile.Plugins is not null))
            throw new ValidationException("Use match-presets with map for per-map actors or package selections.");
        ValidateMapName(profile?.MapName, location, "map-name");
        if (profile!.Name is { } displayName) ValidateName(displayName);
        var ini = IniOverrides.Parse(profile.Ini);
        if (ini.RawArguments.Any()) throw new ValidationException($"{location}.ini cannot use raw; per-map values must be written to INI files using typed overrides.");
        if (ini.Contains("Game", "/Script/TBL.TBLGameMode", "MapListIndex"))
            throw new ValidationException($"{location}.ini cannot change MapListIndex; use starting-map and map-rotation.");
        return ini;
    }

    internal static void ValidateMapName(string? name, string location, string field) {
        if (name is null || !Regex.IsMatch(name, @"\A/?[A-Za-z0-9_-]+(?:/[A-Za-z0-9_-]+)*(?:\.[A-Za-z0-9_-]+)?\z"))
            throw new ValidationException($"{location} requires a {field} containing a short map name or package path, without travel options.");
    }

    internal static string FriendlyName(string name, string mapName) {
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

    internal static void ValidateName(string name, string kind = "Map profile name") {
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name.Any(char.IsControl))
            throw new ValidationException($"{kind} must be nonblank, without surrounding whitespace or control characters.");
    }

    internal static void ValidateProfile(string name) {
        if (name.Length is 0 or > 128 || name is "." or ".." || name.EndsWith('.') ||
            !Regex.IsMatch(name, @"\A[A-Za-z0-9_.-]+\z") ||
            Regex.IsMatch(name.Split('.')[0], @"\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new ValidationException("Map profile directory must be a portable folder name, without path separators, surrounding whitespace, trailing dots, or reserved device names.");
    }

    internal static MapRotation? Resolve(List<MapRotationEntry>? entries, Dictionary<string, MapProfile?>? profiles, string? startingMap) {
        if (entries is null) return null;
        if (entries.Count == 0) throw new ValidationException("map-rotation must contain at least one map.");
        var maps = new List<string>();
        var overrides = new List<IniOverrides>();
        var names = new List<string>();
        var directories = new List<string>();
        var uniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var uniqueDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var layers = new List<NativeIniLayer>();
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
            var directory = entry!.Profile ?? "inline-" + maps.Count.ToString(CultureInfo.InvariantCulture);
            ValidateProfile(directory);
            if (!uniqueDirectories.Add(directory)) throw new ValidationException("Map profile directory references must be unique within each server (case-insensitive); rename conflicting profile keys.");
            directories.Add(directory);
            names.Add(displayName);
            maps.Add(profile!.MapName!);
            overrides.Add(ini);
            layers.Add(new(entry.Profile is null ? "indices" : "profiles",
                entry.Profile ?? (maps.Count - 1).ToString(CultureInfo.InvariantCulture), ini));
        }
        var index = startingMap is null ? 0 : maps.FindIndex(map => map.Equals(startingMap, StringComparison.OrdinalIgnoreCase));
        if (index < 0) throw new ValidationException("starting-map must match a map-name in map-rotation.");
        return new MapRotation(maps.ToArray(), index, overrides.ToArray(), names.ToArray(), directories.ToArray()) { Layers = layers.AsReadOnly() };
    }

    internal void Apply(string directory) {
        var writer = new StructuredINIWriter();
        BufferInto(writer);
        IniPatches.Write(directory, new(StringComparer.OrdinalIgnoreCase) { ["Game"] = writer }, isConfigRoot: true);
    }

    internal void BufferInto(StructuredINIWriter writer) {
        if (!writer.BufferWrite(new RotationIni(Maps.ToArray(), StartIndex), merge: true))
            throw new IOException("Could not serialize the map rotation.");
    }

    internal MapRotation ReadSavedIndex(string gameIni) {
        if (!File.Exists(gameIni)) return this;
        int index;
        try { index = new StructuredINIParser().Deserialize<SavedRotationIni>(File.ReadAllText(gameIni)).MapListIndex; }
        catch (Exception ex) when (ex is FormatException or OverflowException) {
            throw new ArgumentException("Saved Game.ini MapListIndex must be an integer; set starting-map to override it.");
        }
        if (index == -1) return this; // Core's default means no saved selection.
        if (index < 0 || index >= Maps.Count)
            throw new ArgumentException("Saved Game.ini MapListIndex is outside map-rotation; set starting-map to select a valid slot.");
        return this with { StartIndex = index };
    }
}
