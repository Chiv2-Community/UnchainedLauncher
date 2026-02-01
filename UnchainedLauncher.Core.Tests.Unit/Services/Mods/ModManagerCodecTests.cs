using FluentAssertions;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.PakDir;
using UnchainedLauncher.Core.Tests.Unit.Services.Mods.Registry;
using UnchainedLauncher.Core.Tests.Unit.Utilities;
using Xunit.Abstractions;
using CorePakDir = UnchainedLauncher.Core.Services.PakDir;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Mods {
    public class ModManagerCodecTests : CodecTestBase<ModManager> {
        private static readonly IModRegistry Registry = LocalModRegistryFactory.DefaultModRegistry;
        private const string TestPakDirPath = "TestPakDir";

        public ModManagerCodecTests(ITestOutputHelper testOutputHelper) : base(new ModManagerCodec(Registry), testOutputHelper) { }

        private static CorePakDir.PakDir CreateTestPakDir() =>
            new CorePakDir.PakDir(TestPakDirPath, Enumerable.Empty<ManagedPak>());

        [Fact]
        public void StandardModManager_SerializeAndDeserialize_PreservesData() {
            var enabledMods = new[]
            {
                new ReleaseCoordinates("TestOrg", "TestRepo", "1.0.0"),
                new ReleaseCoordinates("AnotherOrg", "AnotherRepo", "2.0.0")
            };

            var originalManager = new ModManager(Registry, CreateTestPakDir(), enabledMods);

            VerifyCodecRoundtrip(originalManager, manager => {
                manager.EnabledModReleaseCoordinates.Should().BeEquivalentTo(enabledMods);
                manager.ModRegistry.Should().BeSameAs(Registry);
            });
        }

        [Fact]
        public void ModManager_SerializeAndDeserialize_PreservesEmptyEnabledMods() {
            var enabledMods = Array.Empty<ReleaseCoordinates>();
            var originalManager = new ModManager(Registry, CreateTestPakDir(), enabledMods);

            VerifyCodecRoundtrip(originalManager, manager => {
                manager.EnabledModReleaseCoordinates.Should().BeEmpty();
                manager.ModRegistry.Should().BeSameAs(Registry);
            });
        }
    }
}