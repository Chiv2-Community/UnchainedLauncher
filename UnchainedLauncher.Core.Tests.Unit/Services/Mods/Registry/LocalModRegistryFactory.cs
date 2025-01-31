using UnchainedLauncher.Core.Services.Mods.Registry;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Mods.Registry {
    public static class LocalModRegistryFactory {
        public const string DefaultModManagerPath = "TestData/ModRegistries/DefaultModRegistry";
        public const string AlternateModManagerPath = "TestData/ModRegistries/AlternateLocalModRegistry";

        public static LocalModRegistry DefaultModRegistry =>
            Create(DefaultModManagerPath);

        public static LocalModRegistry AlternateModRegistry =>
            Create(AlternateModManagerPath);

        public static LocalModRegistry Create(string path) =>
            new LocalModRegistry(path);
    }
}