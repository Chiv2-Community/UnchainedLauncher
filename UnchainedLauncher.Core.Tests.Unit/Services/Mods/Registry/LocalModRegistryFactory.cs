using UnchainedLauncher.Core.Services.Mods.Registry;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Mods.Registry {
    public static class LocalModRegistryFactory {
        public static readonly string DefaultModManagerPath = Path.GetFullPath("TestData/ModRegistries/DefaultModRegistry");
        public static readonly string AlternateModManagerPath = Path.GetFullPath("TestData/ModRegistries/AlternateLocalModRegistry");

        public static LocalModRegistry DefaultModRegistry =>
            Create(DefaultModManagerPath);

        public static LocalModRegistry AlternateModRegistry =>
            Create(AlternateModManagerPath);

        public static LocalModRegistry Create(string path) =>
            new LocalModRegistry(path);
    }
}