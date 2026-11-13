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
    private sealed record Entry([property: JsonRequired] long Length, [property: JsonRequired] long LastWriteTicks,
        [property: JsonRequired] string Hash, [property: JsonRequired] bool VersionRead, [property: JsonRequired] string? Version);
    private readonly string directory;
    private readonly string cachePath;
    private readonly Func<string, Task<string?>> readVersion;
    private readonly Func<Stream, Task<byte[]>> readHash;
    private readonly Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);
    private bool dirty;

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

    internal async Task<string> Hash(string path) {
        var before = Metadata(path);
        var name = Path.GetFileName(path);
        if (entries.TryGetValue(name, out var cached) && Matches(cached, before)) {
            Logger.Info($"Using cached SHA-512 hash for pak '{path}' ({before.Length} bytes).");
            return cached.Hash;
        }
        entries.Remove(name);
        dirty = true;
        var timer = Stopwatch.StartNew();
        Logger.Info($"Hashing pak '{path}' ({before.Length} bytes) using SHA-512.");
        try {
            using var stream = File.OpenRead(path);
            var hash = Convert.ToHexString(await readHash(stream));
            if (before != Metadata(path)) throw new IOException($"Pak changed while hashing: {path}");
            entries[name] = new Entry(before.Length, before.Ticks, hash, false, null);
            Logger.Info($"Finished hashing pak '{path}' ({before.Length} bytes) in {timer.ElapsedMilliseconds} ms; SHA-512: {hash}");
            return hash;
        }
        catch (Exception error) {
            Logger.Error($"Failed to hash pak '{path}' ({before.Length} bytes) after {timer.ElapsedMilliseconds} ms.", error);
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
        if (entry.VersionRead) return entry.Version;
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
        entries[name] = entry with { VersionRead = true, Version = version };
        dirty = true;
        return version;
    }

    public void Dispose() {
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