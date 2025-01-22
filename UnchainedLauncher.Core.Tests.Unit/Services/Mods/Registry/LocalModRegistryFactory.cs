using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Mods.Registry.Downloader;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Mods.Registry {
    public static class LocalModRegistryFactory {
        public const string DEFAULT_MOD_MANAGER_PATH = "TestData/ModRegistries/DefaultModRegistry";
        public const string ALTERNATE_MOD_MANAGER_PATH = "TestData/ModRegistries/AlternateLocalModRegistry";

        public static LocalModRegistry DefaultModRegistry =>
            Create(DEFAULT_MOD_MANAGER_PATH);

        public static LocalModRegistry AlternateModRegistry =>
            Create(ALTERNATE_MOD_MANAGER_PATH);

        public static LocalModRegistry Create(string path) =>
            new LocalModRegistry(
                path,
                new LocalFilePakDownloader(path)
            );
    }
}