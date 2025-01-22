using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Mods {
    public static class LocalModManagerFactory {
        public static IModManager ForRegistry(IModRegistry registry, IEnumerable<ReleaseCoordinates>? enabledMods = null) {
            return new ModManager(
                registry,
                enabledMods ?? Enumerable.Empty<ReleaseCoordinates>()
            );
        }
    }
}