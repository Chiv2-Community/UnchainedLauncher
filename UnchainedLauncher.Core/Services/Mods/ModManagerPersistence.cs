using System.Text;

namespace UnchainedLauncher.Core.Services.Mods;

public sealed class ModManagerPersistence {
    private readonly ModManager _manager;
    private readonly ModManagerCodec _codec;
    private readonly string _path;
    private readonly object _gate = new();

    public ModManagerPersistence(ModManager manager, string path) {
        _manager = manager;
        _codec = new ModManagerCodec(manager.ModRegistry);
        _path = Path.GetFullPath(path);
        manager.ModEnabled += (_, _) => Save();
        manager.ModDisabled += _ => Save();
        if (manager.PakDir is PakDir paks) paks.OwnershipChanged += (_, _) => Save();
    }

    public void Save() {
        lock (_gate) {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                    var bytes = Encoding.UTF8.GetBytes(_codec.Serialize(_manager));
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporary, _path, overwrite: true);
            }
            finally {
                File.Delete(temporary);
            }
        }
    }
}