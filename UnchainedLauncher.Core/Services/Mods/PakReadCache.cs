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
        int VersionScanRevision = 0, string? ScanIdentity = null, string? ScanJson = null);
    private static readonly object CacheFileLock = new();
    private readonly string directory;
    private readonly string cachePath;
    private readonly Func<string, Task<string?>> readVersion;
    private readonly Func<Stream, Task<byte[]>> readHash;
    private readonly Dictionary<string, Entry> entries;
    private readonly HashSet<string> scansWritten = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> versionsWritten = new(StringComparer.OrdinalIgnoreCase);
    private bool dirty;
    private TimeSpan hashingTime;
    private bool hashFailed;

    internal PakReadCache(string directory, Func<string, Task<string?>>? readVersion = null,
        Func<Stream, Task<byte[]>>? readHash = null) {
        this.directory = directory;
        this.readVersion = readVersion ?? PakBlueprintVersion.Read;
        this.readHash = readHash ?? (async stream => await SHA512.HashDataAsync(stream));
        cachePath = Path.Combine(directory, ".unchained", "pak-read-cache.json");
        lock (CacheFileLock) entries = Load();
    }

    private Dictionary<string, Entry> Load() {
        var loadedEntries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        try {
            if (!File.Exists(cachePath)) return loadedEntries;
            var loaded = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(cachePath));
            if (loaded == null) { dirty = true; return loadedEntries; }
            foreach (var (name, entry) in loaded) {
                if (entry == null || name != Path.GetFileName(name) || entry.Length < 0 || entry.LastWriteTicks <= 0
                    || entry.Hash == null || entry.Hash.Length != 128 || !entry.Hash.All(Uri.IsHexDigit)
                    || (!entry.VersionRead && entry.Version != null)) { dirty = true; continue; }
                loadedEntries.Add(name, entry);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException) {
            loadedEntries.Clear();
            dirty = true;
            Logger.Warn($"Could not read pak cache: {error.Message}");
        }
        return loadedEntries;
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
            Invalidate(name);
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
        Invalidate(name);
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
        versionsWritten.Add(name);
        dirty = true;
        return version;
    }

    internal string? CachedHash(string path) => entries.TryGetValue(Path.GetFileName(path), out var entry)
        && Matches(entry, Metadata(path)) ? entry.Hash : null;

    internal string? ReadScan(string path, string hash, string identity) {
        Validate(path, hash);
        var entry = entries[Path.GetFileName(path)];
        return entry.ScanIdentity == identity ? entry.ScanJson : null;
    }

    internal void WriteScan(string path, string hash, string identity, string json) {
        Validate(path, hash);
        var name = Path.GetFileName(path);
        entries[name] = entries[name] with { ScanIdentity = identity, ScanJson = json };
        scansWritten.Add(name);
        dirty = true;
    }

    internal void Prune() {
        if (!Directory.Exists(directory)) {
            dirty |= entries.Count > 0;
            entries.Clear();
            scansWritten.Clear();
            versionsWritten.Clear();
            return;
        }
        var current = Directory.EnumerateFiles(directory, "*.pak").Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in entries.Keys.ToArray()) {
            try {
                if (current.Contains(name) && Matches(entries[name], Metadata(Path.Combine(directory, name)))) continue;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            Invalidate(name);
        }
    }

    private void Invalidate(string name) {
        entries.Remove(name);
        scansWritten.Remove(name);
        versionsWritten.Remove(name);
        dirty = true;
    }

    public void Dispose() {
        lock (CacheFileLock) Save();
    }

    private void Save() {
        if (!hashFailed)
            Logger.Info(FormattableString.Invariant($"Hashed all new paks in {hashingTime.TotalSeconds:0.000} s"));
        try { Prune(); }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }
        if (!dirty || !Directory.Exists(directory)) return;
        // Other clients may have completed scans or version reads since this instance loaded.
        foreach (var (name, latest) in Load()) {
            if (!entries.TryGetValue(name, out var local)) entries[name] = latest;
            else if (local.Hash.Equals(latest.Hash, StringComparison.OrdinalIgnoreCase)
                && local.Length == latest.Length && local.LastWriteTicks == latest.LastWriteTicks) {
                var version = versionsWritten.Contains(name) || !latest.VersionRead ? local : latest;
                var scan = scansWritten.Contains(name) || latest.ScanJson == null ? local : latest;
                entries[name] = local with {
                    VersionRead = version.VersionRead,
                    Version = version.Version,
                    VersionScanRevision = version.VersionScanRevision,
                    ScanIdentity = scan.ScanIdentity,
                    ScanJson = scan.ScanJson
                };
            }
        }
        var temporary = cachePath + "." + Guid.NewGuid() + ".tmp";
        try {
            Prune();
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