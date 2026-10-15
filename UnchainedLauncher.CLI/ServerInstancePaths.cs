using UnchainedLauncher.Core.Services.Mods;

namespace UnchainedLauncher.CLI;

public sealed record ServerInstancePaths(string Root) {
    public string Paks => Path.Combine(Root, "Paks");
    public string Plugins => Path.Combine(Root, "Plugins");
    public string ModMetadata => Path.Combine(Root, "cli_mod_manager.json");

    internal IDisposable AcquireLease() {
        Directory.CreateDirectory(Root);
        try {
            return new FileStream(Path.Combine(Root, ".instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex) {
            throw new IOException("Cannot lock the server instance directory; another launcher may be using the same saved profile.", ex);
        }
    }

    internal PakDir CreatePakDir(string basePakDirectory) {
        Directory.CreateDirectory(Paks);
        return new PakDir(Paks, [], Path.Combine(basePakDirectory, "pakchunk0-WindowsNoEditor.sig"));
    }
}