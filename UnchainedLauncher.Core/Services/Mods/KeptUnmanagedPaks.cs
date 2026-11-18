using log4net;
using System.Text.Json;

namespace UnchainedLauncher.Core.Services.Mods;

internal sealed class KeptUnmanagedPaks(string pakDirectory) {
    private static readonly ILog Logger = LogManager.GetLogger(nameof(KeptUnmanagedPaks));
    private string FilePath => Path.Combine(pakDirectory, ".unchained", "kept-unmanaged-paks.json");
    private readonly Dictionary<string, string> _hashes = new(StringComparer.OrdinalIgnoreCase);

    internal void Load() {
        if (!File.Exists(FilePath)) return;
        try {
            var hashes = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath))
                ?? throw new JsonException("Empty kept-pak dictionary");
            foreach (var entry in hashes) _hashes.Add(entry.Key, entry.Value);
        }
        catch (Exception error) when (error is JsonException or ArgumentException) {
            _hashes.Clear();
            Logger.Warn($"Could not read kept-pak choices; confirmation is required again: {FilePath}", error);
        }
    }

    internal bool Contains(string name, string hash) => _hashes.TryGetValue(name, out var keptHash)
        && hash.Equals(keptHash, StringComparison.OrdinalIgnoreCase);

    internal void Save(Dictionary<string, string> hashes) {
        if (hashes.Count == _hashes.Count && hashes.All(entry => Contains(entry.Key, entry.Value))) return;
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporaryPath = FilePath + "." + Guid.NewGuid() + ".tmp";
        try {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                JsonSerializer.Serialize(stream, hashes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        finally {
            File.Delete(temporaryPath);
        }
    }
}