using CUE4Parse.UE4.Pak.Objects;
using Newtonsoft.Json;
using System.Security.Cryptography;
using System.Text;
using UnchainedLauncher.UnrealModScanner.Config;
using UnchainedLauncher.UnrealModScanner.PakScanning;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnrealModScanner.Models;

namespace UnchainedLauncher.Core.Services.Mods;

// One filename inventory owns hashes, Blueprint versions and full scanner output.
public static class PakScanCache {
    // Revision 4 refreshes old full scans once, retaining hash/version entries.
    private const int ScannerRevision = 4;
    private static readonly JsonSerializerSettings Settings = new() {
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
        DateParseHandling = DateParseHandling.None
    };

    private static string Identity(ScanOptions options) => ScannerRevision + ":" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(options))));

    public static Task<ModScanResult> ScanAsync(string directory, ScanOptions options, IProgress<double>? progress = null,
        bool cachedOnly = false) => ScanAsync(directory, options, null, progress, cachedOnly);

    internal static Task<ModScanResult> ScanAsync(string directory, ScanOptions options,
        Func<IReadOnlyDictionary<string, string>, Task<ModScanResult>>? scan,
        IProgress<double>? progress = null, bool cachedOnly = false) => Task.Run(async () => {
            using var cache = new PakReadCache(directory);
            cache.Prune();
            var inventory = PakScanDependencies.Snapshot(directory);
            var identity = Identity(options);
            var changedIndexes = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            string[] ReadIndex(string name) {
                var container = Path.GetExtension(name).Equals(".ucas", StringComparison.OrdinalIgnoreCase)
                    ? Path.ChangeExtension(name, ".utoc") : name;
                if (!inventory.ContainsKey(container) && name.EndsWith(".ucas", StringComparison.OrdinalIgnoreCase)) {
                    container = System.Text.RegularExpressions.Regex.Replace(name, @"_s\d+\.ucas$", ".utoc",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    // A stray payload without a mountable index cannot affect resolution.
                    if (!inventory.ContainsKey(container)) return [];
                }
                if (changedIndexes.TryGetValue(container, out var saved)) return saved;
                if (!inventory.ContainsKey(container)) throw new IOException($"Missing container index: {container}");
                using var provider = FilteredFileProvider.CreateFromOptions(options, ScanMode.Mods, directory);
                provider.InitializeContainers([Path.Combine(directory, container)]);
                if (!provider.MountedVfs.Any(vfs => Path.GetFileName(vfs.Path) == container))
                    throw new IOException($"Container could not be mounted: {container}");
                return changedIndexes[container] = provider.Files.Keys.ToArray();
            }
            var result = new ModScanResult();
            var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var paths = Directory.GetFiles(directory, "*.pak")
                .Where(path => !options.VanillaPakNames.Contains(Path.GetFileName(path))).ToArray();
            foreach (var path in paths) {
                var fullPath = Path.GetFullPath(path);
                var hash = cachedOnly ? cache.CachedHash(fullPath) : await cache.Hash(fullPath);
                if (hash == null) continue;
                hashes[fullPath] = hash;
                var json = cache.ReadScan(fullPath, hash, identity);
                var dependencies = cache.ReadDependencies(fullPath);
                if (json != null && (dependencies == null || !dependencies.Matches(inventory,
                    name => cachedOnly ? null : ReadIndex(name)))) json = null;
                PakScanResult? pak = null;
                if (json != null) {
                    try {
                        pak = JsonConvert.DeserializeObject<PakScanResult>(json, Settings);
                        if (pak?._Markers.Any(marker => marker == null || marker.Blueprints.Any(child => child == null)) == true) pak = null;
                        if (pak != null) {
                            var check = new ModScanResult();
                            check.Paks[Path.GetFileName(path)] = pak;
                            _ = UnchainedLauncher.UnrealModScanner.Services.ModManifestConverter.ProcessModScan(check);
                        }
                    }
                    catch (JsonException) { pak = null; }
                    catch (NullReferenceException) { pak = null; }
                    catch (ArgumentException) { pak = null; }
                }
                if (pak != null && string.Equals(pak.PakHash, hash, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(pak.PakPath, fullPath, StringComparison.OrdinalIgnoreCase)) {
                    result.Paks[Path.GetFileName(path)] = pak;
                    if (!PakScanDependencies.Same(dependencies!.Inventory, inventory))
                        cache.WriteScan(fullPath, hash, identity, json!, dependencies with { Inventory = inventory });
                }
                else pending.Add(fullPath);
            }
            if (!cachedOnly && pending.Count > 0) {
                ModScanResult scanned;
                var dependencies = new Dictionary<string, PakScanDependencies>(StringComparer.OrdinalIgnoreCase);
                if (scan != null) scanned = await scan(pending.ToDictionary(path => path, path => hashes[path]));
                else {
                    using var provider = FilteredFileProvider.CreateFromOptions(options, ScanMode.Mods, directory,
                        kv => kv.Value is FPakEntry entry && pending.Contains(Path.GetFullPath(entry.PakFileReader.Path)));
                    scanned = await ScannerFactory.CreateModScanner(options).RunScanAsync(provider, options, progress!,
                        path => hashes[Path.GetFullPath(path)], failOnErrors: true);
                    if (pending.Any(path => !provider.MountedVfs.Any(vfs => Path.GetFullPath(vfs.Path) == path)))
                        throw new IOException("Not all paks could be mounted; scan results were not cached.");
                    var globalPaths = provider.Files.Keys.Where(PakScanDependencies.IsGlobal).ToArray();
                    // CUE4Parse infers /Game's root from the mounted index. Mixed projects
                    // have ambiguous inference, so retain a conservative dependency there.
                    var mixedProjects = provider.Files.Keys.Select(PakScanDependencies.ProjectRoot)
                        .Where(root => root != null).Distinct(StringComparer.Ordinal).Take(2).Count() > 1;
                    foreach (var path in pending) {
                        // Include the owning container's index even if all its assets are shadowed.
                        var own = provider.MountedVfs.Single(vfs => Path.GetFullPath(vfs.Path) == path);
                        var lookups = provider.GetScanLookups(path).Concat(own.Files.Keys)
                            .Concat(globalPaths).ToHashSet(StringComparer.Ordinal);
                        var contributors = provider.MountedVfs.Where(vfs => mixedProjects || vfs.Files.Keys.Any(lookups.Contains))
                            .Select(vfs => Path.GetFileName(vfs.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        foreach (var io in provider.MountedVfs.OfType<CUE4Parse.UE4.IO.IoStoreReader>()
                            .Where(io => contributors.Contains(Path.GetFileName(io.Path))))
                            contributors.UnionWith(io.ContainerStreams.Select(stream => Path.GetFileName(stream.Name)));
                        var usesIoStore = provider.ScanUsesIoStore(path);
                        if (usesIoStore) {
                            contributors.UnionWith(inventory.Keys.Where(name => Path.GetExtension(name).ToLowerInvariant() is ".utoc" or ".ucas"));
                        }
                        dependencies[path] = new(inventory, lookups.ToArray(), contributors.ToArray(), provider.ProjectName, usesIoStore);
                    }
                }
                foreach (var path in pending) {
                    var name = Path.GetFileName(path);
                    // Empty paks are reusable results too.
                    var pak = scanned.Paks.TryGetValue(name, out var found) ? found : new PakScanResult { PakPath = path, PakHash = hashes[path] };
                    cache.Validate(path, hashes[path]);
                    result.Paks[name] = pak;
                }
                // Validate the entire snapshot before publishing any new scan entries.
                if (!PakScanDependencies.Same(PakScanDependencies.Snapshot(directory), inventory)) throw new IOException("Pak inventory changed during scanning.");
                foreach (var path in paths) cache.Validate(path, hashes[Path.GetFullPath(path)]);
                foreach (var path in pending) cache.WriteScan(path, hashes[path], identity,
                    JsonConvert.SerializeObject(result.Paks[Path.GetFileName(path)], Settings),
                    dependencies.GetValueOrDefault(path) ?? new(inventory, [], []));
            }
            cache.Prune();
            if (!PakScanDependencies.Same(PakScanDependencies.Snapshot(directory), inventory)) throw new IOException("Pak inventory changed during scanning.");
            foreach (var pak in result.Paks.Values) cache.Validate(pak.PakPath, pak.PakHash!);
            progress?.Report(100);
            return result;
        });

    public static Task<Dictionary<string, string?>> GetHashesAsync(IEnumerable<string> paths) => Task.Run(async () => {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in paths.Select(Path.GetFullPath).GroupBy(Path.GetDirectoryName, StringComparer.OrdinalIgnoreCase)) {
            using var cache = new PakReadCache(group.Key!);
            foreach (var path in group) {
                try { result[path] = await cache.Hash(path); }
                catch (IOException) { result[path] = null; }
                catch (UnauthorizedAccessException) { result[path] = null; }
            }
            foreach (var path in group.Where(path => result[path] != null)) {
                try { cache.Validate(path, result[path]!); }
                catch (IOException) { result[path] = null; }
                catch (UnauthorizedAccessException) { result[path] = null; }
            }
        }
        return result;
    });
}