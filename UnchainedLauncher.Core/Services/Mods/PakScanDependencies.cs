namespace UnchainedLauncher.Core.Services.Mods;

// Lookup paths include misses and payloads; contributors include shadowed candidates.
internal sealed record PakScanDependencies(Dictionary<string, PakScanDependencies.Stamp> Inventory,
    string[] Paths, string[] Contributors, string? ProjectName = null, bool UsesIoStore = false) {
    internal sealed record Stamp(long Length, long Ticks);

    internal static Dictionary<string, Stamp> Snapshot(string directory) => Directory.EnumerateFiles(directory)
        .Where(path => Path.GetExtension(path).ToLowerInvariant() is ".pak" or ".utoc" or ".ucas" or ".uproject")
        .ToDictionary(path => Path.GetFileName(path)!, path => {
            var file = new FileInfo(path);
            return new Stamp(file.Length, file.LastWriteTimeUtc.Ticks);
        }, StringComparer.OrdinalIgnoreCase);

    internal static bool Same(Dictionary<string, Stamp> left, Dictionary<string, Stamp> right) =>
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value);

    internal bool Matches(Dictionary<string, Stamp> current, Func<string, string[]?> index) {
        if (Inventory == null || Paths == null || Contributors == null
            || Inventory.Any(pair => pair.Key != Path.GetFileName(pair.Key) || pair.Value == null
                || pair.Value.Length < 0 || pair.Value.Ticks <= 0)
            || Paths.Any(string.IsNullOrEmpty) || Contributors.Any(string.IsNullOrEmpty)) return false;
        if (Contributors.Any(name => !Inventory.TryGetValue(name, out var before)
            || !current.TryGetValue(name, out var after) || before != after)) return false;
        var lookups = Paths.ToHashSet(StringComparer.Ordinal);
        if (lookups.Count == 0) return true;
        foreach (var (name, stamp) in current) {
            if (Inventory.TryGetValue(name, out var before) && before == stamp) continue;
            // IoPackage resolves transitive imports by numeric ID, not a pathname.
            if (UsesIoStore && Path.GetExtension(name).ToLowerInvariant() is ".utoc" or ".ucas") return false;
            if (Path.GetExtension(name).Equals(".uproject", StringComparison.OrdinalIgnoreCase)) return false;
            var files = index(name);
            if (files == null || files.Any(path => lookups.Contains(path) || IsGlobal(path)
                || (ProjectName != null && ProjectRoot(path) is { } root && root != ProjectName))) return false;
        }
        return !Inventory.Keys.Except(current.Keys, StringComparer.OrdinalIgnoreCase)
            .Any(name => Path.GetExtension(name).Equals(".uproject", StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsGlobal(string path) => path.EndsWith(".uplugin", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".upluginmanifest", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".uproject", StringComparison.OrdinalIgnoreCase);

    internal static string? ProjectRoot(string path) {
        var slash = path.IndexOf('/');
        return slash > 0 && !path[..slash].EndsWith("Engine", StringComparison.OrdinalIgnoreCase) ? path[..slash] : null;
    }
}