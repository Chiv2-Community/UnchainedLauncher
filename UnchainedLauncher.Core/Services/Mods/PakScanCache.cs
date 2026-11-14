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
    private const int ScannerRevision = 1;
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
        var identity = Identity(options);
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
                && string.Equals(pak.PakPath, fullPath, StringComparison.OrdinalIgnoreCase)) result.Paks[Path.GetFileName(path)] = pak;
            else pending.Add(fullPath);
        }
        if (!cachedOnly && pending.Count > 0) {
            ModScanResult scanned;
            if (scan != null) scanned = await scan(pending.ToDictionary(path => path, path => hashes[path]));
            else {
                using var provider = FilteredFileProvider.CreateFromOptions(options, ScanMode.Mods, directory,
                    kv => kv.Value is FPakEntry entry && pending.Contains(Path.GetFullPath(entry.PakFileReader.Path)));
                scanned = await ScannerFactory.CreateModScanner(options).RunScanAsync(provider, options, progress!,
                    path => hashes[Path.GetFullPath(path)], failOnErrors: true);
                if (pending.Any(path => !provider.MountedVfs.Any(vfs => Path.GetFullPath(vfs.Path) == path)))
                    throw new IOException("Not all paks could be mounted; scan results were not cached.");
            }
            foreach (var path in pending) {
                var name = Path.GetFileName(path);
                // Empty paks are reusable results too.
                var pak = scanned.Paks.TryGetValue(name, out var found) ? found : new PakScanResult { PakPath = path, PakHash = hashes[path] };
                cache.Validate(path, hashes[path]);
                result.Paks[name] = pak;
            }
            // Validate the entire snapshot before publishing any new scan entries.
            foreach (var path in paths) cache.Validate(path, hashes[Path.GetFullPath(path)]);
            foreach (var path in pending) cache.WriteScan(path, hashes[path], identity,
                JsonConvert.SerializeObject(result.Paks[Path.GetFileName(path)], Settings));
        }
        cache.Prune();
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
