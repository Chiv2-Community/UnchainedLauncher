using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace UnchainedLauncher.CLI;

public class SettingsBlock {
    public IniSource? Ini { get; set; }
    public List<string>? ModActors { get; set; }
    public List<string>? Mods { get; set; }
    public List<string>? Plugins { get; set; }
}

public class ScopedSettings : SettingsBlock {
    public List<string>? Registries { get; set; }
    public Dictionary<string, SettingsBlock?>? MapSettings { get; set; }
    public Dictionary<string, MapProfile?>? MatchPresets { get; set; }
}

internal sealed record ResolvedServerSettings(IniOverrides Ini, IReadOnlyList<string> Actors,
    ModSetupConfiguration Packages, MapRotation? Rotation);

internal static class SettingsResolution {
    internal static bool IsModern(ServerConfiguration configuration, ServerSettings settings) =>
        configuration.GlobalSettings is not null || settings.MatchPresets is not null || settings.MapSettings is not null ||
        settings.Ini?.DirectoryPath is not null || settings.Ini?.Files.Count > 0 ||
        settings.Mods is not null || settings.Plugins is not null || settings.Registries is not null ||
        settings.MapRotation?.Any(entry => entry?.Inline is { } inline && (inline.Map is not null || inline.Preset is not null || inline.ModActors is not null)) == true;

    internal static ResolvedServerSettings Resolve(ScopedSettings? global, ServerSettings server) {
        global ??= new ScopedSettings();
        ValidateRules(global);
        ValidateRules(server);
        var presets = Rules(global.MatchPresets, server.MatchPresets).ToArray();
        foreach (var (key, preset) in presets) {
            if (string.IsNullOrWhiteSpace(key) || key != key.Trim() || preset is null)
                throw new ValidationException("match-presets must have nonblank keys and settings mappings.");
            ValidatePattern(key, "match-presets");
            ValidateBlock(preset);
            if (preset.Preset is not null) throw new ValidationException("match-presets cannot reference another preset.");
            if (preset.MapName is not null) throw new ValidationException("Use map instead of map-name in match-presets.");
        }
        var always = IniOverrides.Parse(global.Ini).Merge(IniOverrides.Parse(server.Ini));
        var mapRules = Rules(global.MapSettings, server.MapSettings).ToArray();
        var nativeLayers = mapRules.Select(pair => new NativeIniLayer("maps", pair.Key, IniOverrides.Parse(pair.Value!.Ini)))
            .Concat(presets.Select(pair => new NativeIniLayer("profiles", pair.Key, IniOverrides.Parse(pair.Value!.Ini)))).ToList();
        foreach (var layer in nativeLayers) always.ValidateMapOverrides(layer.Ini);
        var actors = Actors([global, server]);
        var packageLayers = new List<SettingsBlock> { global, server };
        var entries = server.MapRotation;
        if (entries is { Count: 0 }) throw new ValidationException("map-rotation must contain at least one map.");
        if (entries is null && (global.MapSettings?.Count > 0 || server.MapSettings?.Count > 0))
            throw new ValidationException("map-settings requires an explicit map-rotation so wildcard rules resolve against the actual runtime slots.");
        var maps = new List<string>();
        var labels = new List<string>();
        var directories = new List<string>();
        var overrides = new List<IniOverrides>();
        var slotActors = new List<IReadOnlyList<string>>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selectedProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries ?? []) {
            if (entry is null) throw new ValidationException("map-rotation entries cannot be null.");
            var reference = entry.Profile ?? entry.Inline?.Preset;
            if (reference is not null) MapRotation.ValidateName(reference, "Match-preset reference");
            if (reference?.IndexOfAny(['~', '?', '*']) >= 0)
                throw new ValidationException("map-rotation preset references must be concrete names; put wildcard rules under match-presets.");
            var selected = reference ?? "inline-" + maps.Count.ToString(CultureInfo.InvariantCulture);
            MapRotation.ValidateProfile(selected);
            var matchingPresets = presets.Where(pair => Matches(pair.Key, selected)).Select(pair => pair.Value!).ToArray();
            if (reference is not null && matchingPresets.Length == 0)
                throw new ValidationException($"Unknown match-preset '{reference}'; define it under match-presets.");
            var inline = entry.Inline;
            if (inline?.MapName is not null)
                throw new ValidationException("Use map instead of map-name in layered settings and match-presets.");
            var map = inline?.Map ?? matchingPresets.LastOrDefault(preset => preset.Map is not null)?.Map;
            var location = $"map-rotation entry {maps.Count + 1}" + (reference is null ? "" : $" (preset '{reference}')");
            if (string.IsNullOrWhiteSpace(map))
                throw new ValidationException($"{location} requires a map after applying all matching configurations; set map on the entry or a matching match-preset.");
            MapRotation.ValidateMapName(map, location, "map");
            var label = inline?.Name ?? matchingPresets.LastOrDefault(preset => preset.Name is not null)?.Name ?? MapRotation.FriendlyName(reference ?? MapLeaf(map), map);
            MapRotation.ValidateName(label, "Map rotation name");
            if (!names.Add(label)) throw new ValidationException($"Map rotation names must be unique within each server (case-insensitive); duplicate name '{label}'.");
            var layers = new List<SettingsBlock> { global, server };
            layers.AddRange(mapRules
                .Where(pair => Matches(pair.Key, MapLeaf(map!))).Select(pair => pair.Value!));
            layers.AddRange(matchingPresets);
            if (inline is not null) layers.Add(inline);
            foreach (var layer in layers) ValidateBlock(layer);
            var inlineIni = IniOverrides.Parse(inline?.Ini);
            // Validate restoration without composing runtime map/profile/index layers.
            always.ValidateMapOverrides(inlineIni);
            overrides.Add(inlineIni);
            maps.Add(map!);
            labels.Add(label);
            if (!selectedProfiles.Add(selected)) {
                var alias = ".-_abcdefghijklmnopqrstuvwxyz0123456789".Select(character => selected + character + "slot-" + (maps.Count - 1).ToString(CultureInfo.InvariantCulture))
                    .FirstOrDefault(candidate => candidate.Length <= 128 && !selectedProfiles.Contains(candidate) &&
                        !presets.Any(pair => pair.Key.Equals(candidate, StringComparison.OrdinalIgnoreCase)) &&
                        presets.Where(pair => pair.Key.EndsWith('~')).All(pair => Matches(pair.Key, selected) == Matches(pair.Key, candidate)));
                if (alias is null) throw new ValidationException("Repeated preset references require distinct native profile names; use distinct preset references to avoid wildcard collisions.");
                foreach (var pair in presets.Where(pair => pair.Key.Equals(selected, StringComparison.OrdinalIgnoreCase)))
                    nativeLayers.Add(new("profiles", alias, IniOverrides.Parse(pair.Value!.Ini)));
                selected = alias;
                selectedProfiles.Add(selected);
            }
            directories.Add(selected);
            if (inline is not null) nativeLayers.Add(new("indices", (maps.Count - 1).ToString(CultureInfo.InvariantCulture), inlineIni));
            slotActors.Add(Actors(layers));
            packageLayers.AddRange(layers.Skip(2));
        }
        var start = server.ExplicitStartingMap is null ? 0 : maps.FindIndex(map => map.Equals(server.ExplicitStartingMap, StringComparison.OrdinalIgnoreCase));
        if (maps.Count > 0 && start < 0) throw new ValidationException("starting-map must match a map in map-rotation.");
        var rotation = maps.Count == 0 ? null : new MapRotation(maps.AsReadOnly(), start, overrides.AsReadOnly(), labels.AsReadOnly(), directories.AsReadOnly()) {
            Actors = slotActors.AsReadOnly(), Layers = nativeLayers.AsReadOnly()
        };
        var packages = new ModSetupConfiguration {
            Registries = SharedLaunchOptions.Entries(global.Registries).Concat(SharedLaunchOptions.Entries(server.Registries)).Distinct().ToList(),
            Mods = Packages(packageLayers.SelectMany(layer => layer.Mods ?? []), "mod"),
            Dlls = Packages(packageLayers.SelectMany(layer => layer.Plugins ?? []), "plugin")
        };
        return new ResolvedServerSettings(always, actors, packages, rotation);
    }

    private static List<string> Packages(IEnumerable<string> values, string kind) {
        var selected = SharedLaunchOptions.Entries(values).Distinct().ToList();
        var coordinates = selected.Select(SharedLaunchOptions.ParseCoordinates).ToArray();
        if (coordinates.GroupBy(item => (item.Org.ToLowerInvariant(), item.ModuleName.ToLowerInvariant())).Any(group => group.Select(item => item.Version).Distinct().Count() > 1))
            throw new ValidationException($"Conflicting versions of a {kind} are selected across settings or rotation entries.");
        return selected;
    }

    private static IReadOnlyList<string> Actors(IEnumerable<SettingsBlock> layers) =>
        Array.AsReadOnly(SharedLaunchOptions.Entries(layers.SelectMany(layer => layer.ModActors ?? [])).Distinct(StringComparer.Ordinal).ToArray());

    private static string MapLeaf(string map) => map.Split('/').Last().Split('.')[0];

    private static IEnumerable<KeyValuePair<string, T?>> Rules<T>(Dictionary<string, T?>? global, Dictionary<string, T?>? server) where T : SettingsBlock =>
        // Stable sorting retains global/server and declaration order for equal specificity.
        (global ?? []).Where(pair => server?.Keys.Any(key => key.Equals(pair.Key, StringComparison.OrdinalIgnoreCase)) != true).Concat(server ?? [])
            .OrderBy(pair => !pair.Key.EndsWith('~'))
            .ThenBy(pair => pair.Key.TrimEnd('~').Length);

    private static bool Matches(string pattern, string value) => pattern.EndsWith('~')
        ? value.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase)
        : value.Equals(pattern, StringComparison.OrdinalIgnoreCase);

    private static void ValidatePattern(string pattern, string location) {
        if (pattern.Contains('*')) throw new ValidationException($"{location} wildcard patterns use ~ instead of *.");
        if (pattern.Contains('?') || pattern.Count(character => character == '~') > 1 || pattern.Contains('~') && !pattern.EndsWith('~'))
            throw new ValidationException($"{location} supports only one trailing ~ wildcard; ? and middle wildcards are not supported.");
        if (pattern.Length > 128) throw new ValidationException($"{location} folder names must not exceed 128 characters.");
        if (pattern != "~") MapRotation.ValidateProfile(pattern.TrimEnd('~'));
    }

    private static void ValidateRules(ScopedSettings settings) {
        ValidateBlock(settings);
        foreach (var keys in new[] { settings.MapSettings?.Keys.ToArray(), settings.MatchPresets?.Keys.ToArray() })
            if (keys is not null && keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != keys.Length)
                throw new ValidationException("Native map and preset rule names must be unique case-insensitively within each scope.");
        foreach (var (pattern, block) in settings.MapSettings ?? []) {
            if (string.IsNullOrWhiteSpace(pattern) || block is null) throw new ValidationException("map-settings requires nonblank patterns and settings mappings.");
            ValidatePattern(pattern, "map-settings");
            ValidateBlock(block);
        }
    }

    private static void ValidateBlock(SettingsBlock settings) {
        var ini = IniOverrides.Parse(settings.Ini);
        if (ini.RawArguments.Any()) throw new ValidationException("Layered settings cannot use raw INI command-line overrides.");
        if (ini.Contains("Game", "/Script/TBL.TBLGameMode", "MapListIndex")) throw new ValidationException("MapListIndex is managed by map-rotation.");
        _ = Actors([settings]);
        _ = Packages(settings.Mods ?? [], "mod");
        _ = Packages(settings.Plugins ?? [], "plugin");
    }
}
