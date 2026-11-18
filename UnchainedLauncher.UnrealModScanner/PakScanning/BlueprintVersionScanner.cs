using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Versions;
using log4net;
using System.Diagnostics;
using System.Text.RegularExpressions;
using UnchainedLauncher.UnrealModScanner.Config.Games;
using UnchainedLauncher.UnrealModScanner.PakScanning.Orchestrators;

namespace UnchainedLauncher.UnrealModScanner.PakScanning;

public static class BlueprintVersionScanner {
    private static readonly ILog Logger = LogManager.GetLogger(nameof(BlueprintVersionScanner));

    public static Task<IReadOnlyList<string>> ReadVersions(string pakPath) => Task.Run<IReadOnlyList<string>>(() => {
        var timer = Stopwatch.StartNew();
        Logger.Info($"Scanning mod version in pak '{Path.GetFileName(pakPath)}'.");
        var options = GameScanOptions.Chivalry2;
        var path = Path.GetFullPath(pakPath);
        using var stream = File.OpenRead(path);
        using var provider = new FilteredFileProvider(Path.GetDirectoryName(path)!, SearchOption.TopDirectoryOnly,
            options.ScanFilter.CreatePakContentFilter(), options.AesKey,
            new VersionContainer(options.GameVersion));
        provider.InitializePak(path, stream);
        ModScanOrchestrator.InitializeCompression(provider);
        var packages = provider.FilteredFiles
            .Where(file => file.Key.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            .Select(file => provider.LoadPackage(file.Value) as Package
                ?? throw new IOException($"Unsupported mod version package: {file.Key}"));
        var versions = ReadVersions(packages);
        Logger.Info($"Finished scanning mod version in pak '{Path.GetFileName(pakPath)}' in {timer.ElapsedMilliseconds} ms.");
        return versions;
    });

    private static string IndexName(Package package, FPackageIndex index) => index.Index switch {
        > 0 => package.ExportMap[index.Index - 1].ObjectName.Text,
        < 0 => package.ImportMap[-index.Index - 1].ObjectName.Text,
        _ => ""
    };

    private static bool IsMarkerClass(string name) =>
        Regex.IsMatch(name, @"^DA_ModMarker_V[0-9]+_C$", RegexOptions.CultureInvariant);

    internal sealed class Reader {
        private readonly Dictionary<(IFileProvider Provider, string Path), Package?> parents = new();
        private readonly Dictionary<(Package Package, int Index, bool Marker), AncestryResult> ancestry = new();
        private readonly HashSet<(IFileProvider Provider, string Path)> recorded = new();
        private sealed record AncestryResult(bool Matches, (IFileProvider Provider, string Path)[] Dependencies);

        internal Reader(IEnumerable<Package> packages) {
            foreach (var package in packages)
                if (package.Provider is { } provider) parents.TryAdd((provider, Key(provider, package.Name)), package);
        }

        private static string Key(IFileProvider provider, string path) =>
            (provider.FixPath(path) ?? path).Replace('\\', '/');

        private Package? LoadParent(IFileProvider provider, string path, HashSet<(IFileProvider Provider, string Path)> dependencies) {
            dependencies.Add((provider, path));
            var key = (provider, Key(provider, path));
            if (parents.TryGetValue(key, out var cached)) {
                // A reused package still contributes a lookup to the current pak's dependencies.
                Record(provider, path);
                return cached;
            }
            var parent = provider.TryLoadPackage(path, out var loaded) ? loaded as Package : null;
            recorded.Add((provider, path));
            parents.Add(key, parent);
            return parent;
        }

        private void Record(IFileProvider provider, string path) {
            if (recorded.Add((provider, path))) provider.TryGetGameFile(path, out _);
        }

        private bool HasAncestry(Package package, FPackageIndex index, bool marker) {
            if (index.IsNull) return false;
            var key = (package, index.Index, marker);
            if (ancestry.TryGetValue(key, out var cached)) {
                foreach (var (provider, path) in cached.Dependencies) Record(provider, path);
                return cached.Matches;
            }
            var dependencies = new HashSet<(IFileProvider Provider, string Path)>();
            var names = ClassAncestry(package, index, dependencies);
            var matches = marker ? names.Any(IsMarkerClass) : names.Contains("ArgonSDKModBase_C", StringComparer.Ordinal);
            ancestry.Add(key, new(matches, dependencies.ToArray()));
            return matches;
        }

        private IEnumerable<string> ClassAncestry(Package package, FPackageIndex index,
            HashSet<(IFileProvider Provider, string Path)> dependencies) {
            var visited = new HashSet<(string, int)>();
            while (!index.IsNull && visited.Add((package.Name.Replace('\\', '/').ToUpperInvariant(), index.Index))) {
                yield return IndexName(package, index);
                if (index.IsExport) {
                    index = package.ExportMap[index.Index - 1].SuperIndex;
                    continue;
                }
                var import = package.ImportMap[-index.Index - 1];
                var outer = import;
                var outers = new HashSet<int>();
                while (outer.OuterIndex is { IsImport: true } parent && outers.Add(parent.Index)) {
                    outer = package.ImportMap[-parent.Index - 1];
                }
                // Resolve parent metadata without deserializing its class or requiring its SDK ancestors to be present.
                if (outer.OuterIndex is not { IsNull: true }
                    || outer.ObjectName.Text.StartsWith("/Script/", StringComparison.Ordinal)
                    || package.Provider == null
                    || LoadParent(package.Provider, outer.ObjectName.Text, dependencies) is not { } parentPackage) yield break;
                var exportIndex = parentPackage.GetExportIndex(import.ObjectName.Text);
                if (exportIndex < 0) yield break;
                package = parentPackage;
                index = new FPackageIndex(package, exportIndex + 1);
            }
        }

        internal IReadOnlyList<string> ReadVersions(IReadOnlyList<Package> packages) {
            recorded.Clear();
            List<string> ReadCandidates(bool marker) {
                var versions = new List<string>();
                foreach (var package in packages)
                    for (var index = 0; index < package.ExportMap.Length; index++) {
                        var export = package.ExportMap[index];
                        var isDefault = export.ObjectName.Text.StartsWith("Default__", StringComparison.Ordinal);
                        var matches = marker
                            ? !isDefault && ((EObjectFlags)export.ObjectFlags & EObjectFlags.RF_ClassDefaultObject) == 0
                                && (HasAncestry(package, export.SuperIndex, marker: true) || HasAncestry(package, export.ClassIndex, marker: true))
                            : isDefault && HasAncestry(package, export.ClassIndex, marker: false);
                        if (!matches) continue;
                        Logger.Debug($"Reading ModVersion from {package.Name}, export {index}.");
                        var value = package.ExportsLazy[index].Value;
                        var properties = marker && value is UClass blueprint ? blueprint.ClassDefaultObject.Load() : value;
                        var version = properties?.GetOrDefault<string>("ModVersion");
                        if (!string.IsNullOrWhiteSpace(version)) versions.Add(version);
                    }
                return versions;
            }
            var markerVersions = ReadCandidates(true);
            return markerVersions.Count > 0 ? markerVersions : ReadCandidates(false);
        }
    }

    internal static IReadOnlyList<string> ReadVersions(IEnumerable<Package> packages) {
        var loaded = packages.ToArray();
        return new Reader(loaded).ReadVersions(loaded);
    }
}