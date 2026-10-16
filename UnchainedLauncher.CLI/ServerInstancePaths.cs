using UnchainedLauncher.Core.Services.Mods;

namespace UnchainedLauncher.CLI;

public sealed record ServerInstancePaths(string Root, string? PakCache = null) {
    public string Paks => Path.Combine(Root, "paks");
    public string Plugins => Path.Combine(Root, "plugins");
    public string ModMetadata => Path.Combine(Root, "cli_mod_manager.json");

    internal IDisposable AcquireLease() {
        Directory.CreateDirectory(Root);
        try {
            return new FileStream(Path.Combine(Root, ".instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex) {
            throw new IOException("Cannot lock the profile asset directory; another launcher may be using the same saved profile.", ex);
        }
    }

    internal PakDir CreatePakDir(string basePakDirectory, CancellationToken cancellationToken = default) {
        Directory.CreateDirectory(Paks);
        return new PakDir(Paks, [], Path.Combine(basePakDirectory, "pakchunk0-WindowsNoEditor.sig"), PakCache, cancellationToken);
    }
}
