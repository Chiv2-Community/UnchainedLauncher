using UnchainedLauncher.Core.Services.Mods;

namespace UnchainedLauncher.CLI;

public sealed record ServerInstancePaths(ProfileAssetPaths Assets) {
    public string Root => Assets.Root;
    public string Paks => Assets.Paks;
    public string Plugins => Assets.Plugins;
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

    internal PakDir CreatePakDir(string installation, CancellationToken cancellationToken = default) =>
        Assets.CreatePakDir(installation, cancellationToken: cancellationToken);
}