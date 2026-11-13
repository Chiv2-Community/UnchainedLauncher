using log4net;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnchainedLauncher.Core.Services.Mods;

// Metadata reuse cannot detect same-size edits that preserve the modification time.
// This is read-result caching, not ownership or permission to keep a pak.
internal sealed class PakReadCache : IDisposable {
    private static readonly ILog Logger = LogManager.GetLogger(nameof(PakReadCache));
    private const int VersionScanRevision = 2;
    private sealed record Entry([property: JsonRequired] long Length, [property: JsonRequired] long LastWriteTicks,
        [property: JsonRequired] string Hash, [property: JsonRequired] bool VersionRead, [property: JsonRequired] string? Version,
        int VersionScanRevision = 0);
    private readonly string directory;
    private readonly string cachePath;
    private readonly Func<string, Task<string?>> readVersion;
    private readonly Func<Stream, Task<byte[]>> readHash;
    private readonly Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);
    private bool dirty;
    private TimeSpan hashingTime;
    private bool hashFailed;

    internal PakReadCache(string directory, Func<string, Task<string?>>? readVersion = null,
        Func<Stream, Task<byte[]>>? readHash = null) {
        this.directory = directory;
        this.readVersion = readVersion ?? PakBlueprintVersion.Read;
        this.readHash = readHash ?? (async stream => await SHA512.HashDataAsync(stream));
        cachePath = Path.Combine(directory, ".unchained", "pak-read-cache.json");
        try {
            if (!File.Exists(cachePath)) return;
            var loaded = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(cachePath));
            if (loaded == null) return;
            foreach (var (name, entry) in loaded) {
                if (entry == null || name != Path.GetFileName(name) || entry.Length < 0 || entry.LastWriteTicks <= 0
                    || entry.Hash == null || entry.Hash.Length != 128 || !entry.Hash.All(Uri.IsHexDigit)
                    || (!entry.VersionRead && entry.Version != null)) continue;
                entries.Add(name, entry);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException) {
            entries.Clear();
            Logger.Warn($"Could not read pak cache: {error.Message}");
        }
    }

    private static (long Length, long Ticks) Metadata(string path) {
        var file = new FileInfo(path);
        return (file.Length, file.LastWriteTimeUtc.Ticks);
    }

    private static bool Matches(Entry entry, (long Length, long Ticks) metadata) =>
        entry.Length == metadata.Length && entry.LastWriteTicks == metadata.Ticks;

    internal static string FormatSize(long bytes) => bytes switch {
        >= 1L << 30 => FormattableString.Invariant($"{bytes / (double)(1L << 30):0.##} GB"),
        >= 1L << 20 => FormattableString.Invariant($"{bytes / (double)(1L << 20):0.##} MB"),
        >= 1L << 10 => FormattableString.Invariant($"{bytes / (double)(1L << 10):0.##} KB"),
        _ => $"{bytes} bytes"
    };

    internal async Task<string> Hash(string path) {
        var timer = new Stopwatch();
        var size = "size unavailable";
        try {
            var before = Metadata(path);
            var name = Path.GetFileName(path);
            if (entries.TryGetValue(name, out var cached) && Matches(cached, before)) {
                Logger.Info($"Using cached SHA-512 hash for pak '{name}'.");
                return cached.Hash;
            }
            size = FormatSize(before.Length);
            entries.Remove(name);
            dirty = true;
            Logger.Info($"Hashing pak '{name}' ({size}).");
            timer.Start();
            using var stream = File.OpenRead(path);
            var hash = Convert.ToHexString(await readHash(stream));
            if (before != Metadata(path)) throw new IOException($"Pak changed while hashing: {path}");
            entries[name] = new Entry(before.Length, before.Ticks, hash, false, null);
            var elapsed = timer.Elapsed;
            hashingTime += elapsed;
            Logger.Debug($"Finished hashing pak '{name}' ({size}) in {elapsed.TotalMilliseconds:0} ms; SHA-512: {hash}");
            return hash;
        }
        catch (Exception error) {
            hashFailed = true;
            Logger.Error($"Failed to hash pak '{path}' ({size}) after {timer.ElapsedMilliseconds} ms.", error);
            throw;
        }
    }

    internal void Validate(string path, string hash) {
        var name = Path.GetFileName(path);
        if (entries.TryGetValue(name, out var entry) && entry.Hash.Equals(hash, StringComparison.OrdinalIgnoreCase)
            && Matches(entry, Metadata(path))) return;
        entries.Remove(name);
        dirty = true;
        throw new IOException($"Pak changed during release confirmation: {path}");
    }

    internal async Task<string?> Version(string path, string hash) {
        Validate(path, hash);
        var name = Path.GetFileName(path);
        var entry = entries[name];
        if (entry.VersionRead && entry.VersionScanRevision == VersionScanRevision) return entry.Version;
        string? version;
        try {
            version = await readVersion(path);
        }
        catch (Exception error) {
            Validate(path, hash);
            Logger.Warn($"Could not read Blueprint versions from {path}: {error.Message}");
            return null;
        }
        Validate(path, hash);
        entries[name] = entry with { VersionRead = true, Version = version, VersionScanRevision = VersionScanRevision };
        dirty = true;
        return version;
    }

    public void Dispose() {
        if (!hashFailed)
            Logger.Info(FormattableString.Invariant($"Hashed all new paks in {hashingTime.TotalSeconds:0.000} s"));
        foreach (var name in entries.Keys.Where(name => !File.Exists(Path.Combine(directory, name))).ToList()) {
            entries.Remove(name);
            dirty = true;
        }
        if (!dirty) return;
        var temporary = cachePath + "." + Guid.NewGuid() + ".tmp";
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                JsonSerializer.Serialize(stream, entries);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, cachePath, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
            Logger.Warn($"Could not save pak cache: {error.Message}");
        }
        finally {
            try {
                File.Delete(temporary);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
                Logger.Warn($"Could not remove temporary pak cache: {error.Message}");
            }
        }
    }
}