using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using CorePakDir = UnchainedLauncher.Core.Services.PakDir;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Mods {
    public static class LocalModManagerFactory {
        private const string TestPakDirPath = "TestPakDir";

        public static IModManager ForRegistry(IModRegistry registry, IEnumerable<ReleaseCoordinates>? enabledMods = null) {
            return new ModManager(
                registry,
                new Core.Services.Mods.PakDir(TestPakDirPath, Enumerable.Empty<ManagedPak>()),
                enabledMods ?? Enumerable.Empty<ReleaseCoordinates>()
            );
        }
    }
}