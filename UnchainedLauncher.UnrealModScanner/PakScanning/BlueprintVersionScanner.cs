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
            .Select(file => provider.LoadPackage(file.Key) as Package
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

    private static IEnumerable<string> ClassAncestry(Package package, FPackageIndex index) {
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
                || !package.Provider.TryLoadPackage(outer.ObjectName.Text, out var loaded)
                || loaded is not Package parentPackage) yield break;
            var exportIndex = parentPackage.GetExportIndex(import.ObjectName.Text);
            if (exportIndex < 0) yield break;
            package = parentPackage;
            index = new FPackageIndex(package, exportIndex + 1);
        }
    }

    internal static IReadOnlyList<string> ReadVersions(IEnumerable<Package> packages) {
        var candidates = new List<(Package Package, int Index, bool Marker)>();
        foreach (var package in packages) {
            for (var index = 0; index < package.ExportMap.Length; index++) {
                var export = package.ExportMap[index];
                if (export.ObjectName.Text.StartsWith("Default__", StringComparison.Ordinal)) {
                    if (ClassAncestry(package, export.ClassIndex).Contains("ArgonSDKModBase_C", StringComparer.Ordinal))
                        candidates.Add((package, index, false));
                }
                else if (((EObjectFlags)export.ObjectFlags & EObjectFlags.RF_ClassDefaultObject) == 0
                    && ClassAncestry(package, export.SuperIndex)
                        .Concat(ClassAncestry(package, export.ClassIndex)).Any(IsMarkerClass)) {
                    candidates.Add((package, index, true));
                }
            }
        }

        var hasMarkers = candidates.Any(candidate => candidate.Marker);
        var versions = new List<string>();
        foreach (var candidate in candidates.Where(candidate => candidate.Marker == hasMarkers)) {
            Logger.Debug($"Reading ModVersion from {candidate.Package.Name}, export {candidate.Index}.");
            var value = candidate.Package.ExportsLazy[candidate.Index].Value;
            var properties = candidate.Marker && value is UClass blueprint ? blueprint.ClassDefaultObject.Load() : value;
            var version = properties?.GetOrDefault<string>("ModVersion");
            if (!string.IsNullOrWhiteSpace(version)) versions.Add(version);
        }
        return versions;
    }
}