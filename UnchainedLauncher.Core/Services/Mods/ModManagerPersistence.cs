using log4net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace UnchainedLauncher.Core.Services.Mods;

public sealed class ModManagerPersistence {
    private static readonly ILog Log = LogManager.GetLogger(typeof(ModManagerPersistence));
    private readonly ModManager _manager;
    private readonly ModManagerCodec _codec;
    private readonly string _path;
    private readonly object _gate = new();

    public ModManagerPersistence(ModManager manager, string path, Action<Exception>? reportSelectionSaveFailure = null) {
        _manager = manager;
        _codec = new ModManagerCodec(manager.ModRegistry);
        _path = Path.GetFullPath(path);
        manager.ModEnabled += (_, _) => SaveSelection();
        manager.ModDisabled += _ => SaveSelection();
        if (manager.PakDir is PakDir paks) {
            paks.Persistence = this;
            paks.MigrationBackupRoot = LegacyOwnershipMigration.BackupRoot(path);
            paks.OwnershipChanged += (_, _) => Save();
        }

        void SaveSelection() {
            try { Save(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                Log.Error("Failed to save mod selection. Saving can be retried.", ex);
                reportSelectionSaveFailure?.Invoke(ex);
            }
        }
    }

    internal string SnapshotHash => OwnershipHash(_codec.Serialize(_manager));

    internal static string OwnershipHash(string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonNode.Parse(json)!["PakDir"]!.ToJsonString())));

    public void Save() {
        lock (_gate) {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                    stream.Write(Encoding.UTF8.GetBytes(_codec.Serialize(_manager)));
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporary, _path, overwrite: true);
            }
            finally { File.Delete(temporary); }
        }
    }
}