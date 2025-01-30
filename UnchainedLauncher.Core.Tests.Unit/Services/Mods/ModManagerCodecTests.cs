using FluentAssertions;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using Xunit;
using UnchainedLauncher.Core.Tests.Unit.Services.Mods.Registry;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Mods
{
    public class ModManagerCodecTests : CodecTestBase<ModManager>
    {
        private static readonly IModRegistry _registry = LocalModRegistryFactory.DefaultModRegistry;

        public ModManagerCodecTests() : base(new ModManagerCodec(_registry)) { }

        [Fact]
        public void StandardModManager_SerializeAndDeserialize_PreservesData()
        {
            var enabledMods = new[]
            {
                new ReleaseCoordinates("TestOrg", "TestRepo", "1.0.0"),
                new ReleaseCoordinates("AnotherOrg", "AnotherRepo", "2.0.0")
            };
            
            var originalManager = new ModManager(_registry, enabledMods);

            VerifyCodecRoundtrip(originalManager, manager => {
                manager.EnabledModReleaseCoordinates.Should().BeEquivalentTo(enabledMods);
                manager.Registry.Should().BeSameAs(_registry);
            });
        }

        [Fact]
        public void ModManager_SerializeAndDeserialize_PreservesEmptyEnabledMods()
        {
            var enabledMods = Array.Empty<ReleaseCoordinates>();
            var originalManager = new ModManager(_registry, enabledMods);

            VerifyCodecRoundtrip(originalManager, manager => {
                manager.EnabledModReleaseCoordinates.Should().BeEmpty();
                manager.Registry.Should().BeSameAs(_registry);
            });
        }
    }
}