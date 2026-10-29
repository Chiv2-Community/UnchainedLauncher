using UnchainedLauncher.Core.Services.Mods;

namespace UnchainedLauncher.CLI;

public sealed record ServerInstancePaths(ProfileAssetPaths Assets) {
    public string Root => Assets.Root;
    public string Paks => Assets.Paks;
    public string Plugins => Assets.Plugins;
    public string ModMetadata => Path.Combine(Root, "cli_mod_manager.json");
    public string Logging => Path.Combine(Root, "logging.yaml");

    internal IDisposable AcquireLease() => Assets.AcquireLease();

    internal PakDir CreatePakDir(string installation, CancellationToken cancellationToken = default) =>
        Assets.CreatePakDir(installation, cancellationToken: cancellationToken);
}
