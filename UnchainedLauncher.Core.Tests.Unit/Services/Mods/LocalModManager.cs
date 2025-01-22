using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Mods.Registry.Downloader;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Mods {
    public static class LocalModManager {
        public static readonly string DEFAULT_MOD_MANAGER_PATH = "TestData/ModRegistries/DefaultModRegistry";
        public static readonly IModManager DEFAULT_MOD_MANAGER = ForPath(DEFAULT_MOD_MANAGER_PATH);
        
        public static IModManager ForPath(string path, IEnumerable<ReleaseCoordinates>? enabledMods = null) {
            return new ModManager(
                new LocalModRegistry(
                    path,
                    new LocalFilePakDownloader(path)
                ),
                enabledMods ?? Enumerable.Empty<ReleaseCoordinates>()
            );
        }
    }
}